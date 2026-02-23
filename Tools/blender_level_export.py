# MonoDreams Level Exporter for Blender 5.0.0
# Exports scene objects to JSON format for use with MonoDreams game engine

bl_info = {
    "name": "MonoDreams Level Exporter",
    "author": "MonoDreams Team",
    "version": (1, 8, 1),
    "blender": (5, 0, 0),
    "location": "File > Export > MonoDreams Level (.json)",
    "description": "Export level objects to JSON for MonoDreams game engine",
    "category": "Import-Export",
}

import bpy
import json
import math
import os
from mathutils import Vector
from bpy.props import StringProperty, FloatProperty, BoolProperty
from bpy_extras.io_utils import ExportHelper


def get_mesh_type(obj):
    """Detect the type of mesh (plane, cube, or custom)."""
    if obj.type != 'MESH':
        return None

    mesh = obj.data
    vertex_count = len(mesh.vertices)
    face_count = len(mesh.polygons)

    # Plane detection: 4 vertices, 1 face (quad) or 2 faces (triangulated)
    if vertex_count == 4 and face_count <= 2:
        return "plane"

    # Cube detection: 8 vertices, 6 faces (quads) or 12 faces (triangulated)
    if vertex_count == 8 and face_count in [6, 12]:
        return "cube"

    # Return the mesh data name for custom meshes
    return mesh.name


def get_mesh_vertices(obj, scale_factor):
    """
    Export mesh vertices transformed to parent-local 2D game coordinates.

    Uses obj.matrix_local to transform each vertex to parent-local 3D space
    (handles rotation, scale, and position offset all at once), then auto-detects
    the mesh plane by comparing Y vs Z extent:
    - XZ plane (standard): game_y = -Z  (top-down view, Z is height)
    - XY plane (fallback):  game_y = -Y  (front view, Y is height)

    Deduplicates by rounded (x, y) and sorts by angle from centroid for
    consistent perimeter ordering. Returns None if fewer than 3 unique 2D vertices.
    """
    if obj.type != 'MESH':
        return None

    mesh = obj.data
    if len(mesh.vertices) < 3:
        return None

    # Transform all vertices to parent-local 3D space first
    transformed = []
    for v in mesh.vertices:
        transformed.append(obj.matrix_local @ v.co)

    # Detect dominant plane: compare Y vs Z extent after transform
    # XY plane mesh (thin in Z): artist sees Y as height → use -Y for game Y
    # XZ plane mesh (thin in Y): artist sees Z as height → use -Z for game Y
    ys = [v.y for v in transformed]
    zs = [v.z for v in transformed]
    range_y = max(ys) - min(ys)
    range_z = max(zs) - min(zs)
    use_y_axis = range_y > range_z

    # Map to 2D game coords
    seen = set()
    verts_2d = []
    for world_v in transformed:
        gx = round(world_v.x * scale_factor, 2)
        gy = round((-world_v.y if use_y_axis else -world_v.z) * scale_factor, 2)
        key = (gx, gy)
        if key not in seen:
            seen.add(key)
            verts_2d.append({"x": gx, "y": gy})

    if len(verts_2d) < 3:
        return None

    # Sort by angle from centroid for consistent perimeter ordering
    cx = sum(v["x"] for v in verts_2d) / len(verts_2d)
    cy = sum(v["y"] for v in verts_2d) / len(verts_2d)
    verts_2d.sort(key=lambda v: math.atan2(v["y"] - cy, v["x"] - cx))

    return verts_2d


def get_texture_image_path(obj):
    """Get the file path of the texture image from the object's material."""
    if not obj.material_slots:
        return None

    for slot in obj.material_slots:
        if not slot.material or not slot.material.use_nodes:
            continue

        # Search through material nodes for image textures
        for node in slot.material.node_tree.nodes:
            if node.type == 'TEX_IMAGE' and node.image:
                image = node.image
                if image.filepath:
                    # Return the filepath (can be relative or absolute)
                    return bpy.path.abspath(image.filepath)
                elif image.packed_file:
                    # Image is packed, return the image name
                    return f"[packed]{image.name}"

    return None


def get_uv_data(obj):
    """
    Extract UV mapping data from a mesh object.

    Returns a dict with:
    - texturePath: path to the texture image (if any)
    - uvLayers: list of UV layers, each containing vertex UV coordinates
    """
    if obj.type != 'MESH':
        return None

    mesh = obj.data

    # Check if mesh has UV layers
    if not mesh.uv_layers:
        return None

    uv_data = {
        "texturePath": get_texture_image_path(obj),
        "uvLayers": []
    }

    # Export each UV layer
    for uv_layer in mesh.uv_layers:
        layer_data = {
            "name": uv_layer.name,
            "uvCoordinates": []
        }

        # UV data is stored per-loop (face corner), not per-vertex
        # We need to map loop indices to vertex indices
        # Build a mapping: vertex_index -> list of UV coords (may have multiple per vertex for seams)
        vertex_uvs = {}

        for poly in mesh.polygons:
            for loop_idx in poly.loop_indices:
                loop = mesh.loops[loop_idx]
                vertex_idx = loop.vertex_index
                uv = uv_layer.data[loop_idx].uv

                # Store UV for this vertex (use first encountered for simplicity)
                if vertex_idx not in vertex_uvs:
                    vertex_uvs[vertex_idx] = {
                        "u": round(uv.x, 6),
                        "v": round(uv.y, 6)
                    }

        # Convert to ordered list by vertex index
        for vertex_idx in sorted(vertex_uvs.keys()):
            layer_data["uvCoordinates"].append({
                "vertexIndex": vertex_idx,
                "u": vertex_uvs[vertex_idx]["u"],
                "v": vertex_uvs[vertex_idx]["v"]
            })

        uv_data["uvLayers"].append(layer_data)

    return uv_data


def get_origin_offset(obj):
    """
    Calculate the origin point position relative to the mesh bounding box.
    Returns normalized coordinates (0-1) where:
    - (0, 0) = bottom-left corner
    - (0.5, 0.5) = center
    - (1, 1) = top-right corner
    """
    if obj.type != 'MESH':
        return {"x": 0.5, "y": 0.5}  # Default to center for non-meshes

    # Get local bounding box (8 corners)
    bbox = obj.bound_box

    # Find min/max in local X and Y
    min_x = min(v[0] for v in bbox)
    max_x = max(v[0] for v in bbox)
    min_y = min(v[1] for v in bbox)
    max_y = max(v[1] for v in bbox)

    # Origin is at local (0,0,0), calculate normalized position within bounding box
    width = max_x - min_x
    height = max_y - min_y

    # Avoid division by zero
    offset_x = (-min_x / width) if width > 0 else 0.5
    offset_y = (-min_y / height) if height > 0 else 0.5

    return {
        "x": round(offset_x, 4),
        "y": round(offset_y, 4)
    }


def get_gp_bounds(obj):
    """
    Calculate world-space bounding box for a Grease Pencil object.
    Returns (min_x, min_y, min_z, max_x, max_y, max_z) in world coordinates.
    """
    if obj.type != 'GREASEPENCIL':
        return None

    bbox = obj.bound_box
    matrix = obj.matrix_world

    # Transform all 8 bounding box corners to world space
    world_corners = [matrix @ Vector(corner) for corner in bbox]

    min_x = min(v.x for v in world_corners)
    max_x = max(v.x for v in world_corners)
    min_y = min(v.y for v in world_corners)
    max_y = max(v.y for v in world_corners)
    min_z = min(v.z for v in world_corners)
    max_z = max(v.z for v in world_corners)

    return (min_x, min_y, min_z, max_x, max_y, max_z)


def get_gp_origin_offset(obj):
    """
    Calculate the origin point position relative to the GP bounding box.
    Returns normalized coordinates (0-1) where:
    - (0, 0) = bottom-left corner
    - (0.5, 0.5) = center
    - (1, 1) = top-right corner
    """
    if obj.type != 'GREASEPENCIL':
        return {"x": 0.5, "y": 0.5}

    # Get local bounding box (8 corners)
    bbox = obj.bound_box

    # Find min/max in local X and Z (Z becomes Y in game)
    min_x = min(v[0] for v in bbox)
    max_x = max(v[0] for v in bbox)
    min_z = min(v[2] for v in bbox)
    max_z = max(v[2] for v in bbox)

    # Origin is at local (0,0,0), calculate normalized position within bounding box
    width = max_x - min_x
    height = max_z - min_z

    # Avoid division by zero
    offset_x = (-min_x / width) if width > 0 else 0.5
    offset_y = (-min_z / height) if height > 0 else 0.5

    return {
        "x": round(offset_x, 4),
        "y": round(offset_y, 4)
    }


def setup_gp_render_camera(scene, gp_obj, padding=1.1):
    """
    Create a temporary orthographic camera positioned to capture the GP object.
    Returns (temp_camera, original_camera).
    """
    bounds = get_gp_bounds(gp_obj)
    if not bounds:
        return None, None

    min_x, min_y, min_z, max_x, max_y, max_z = bounds

    # Calculate center and dimensions
    center_x = (min_x + max_x) / 2
    center_y = (min_y + max_y) / 2
    center_z = (min_z + max_z) / 2

    width = max_x - min_x
    height = max_z - min_z

    # Create temporary camera data
    cam_data = bpy.data.cameras.new(name="TempGPCamera")
    cam_data.type = 'ORTHO'
    # Set orthographic scale to fit the object with padding
    cam_data.ortho_scale = max(width, height) * padding

    # Create camera object
    temp_camera = bpy.data.objects.new(name="TempGPCamera", object_data=cam_data)
    scene.collection.objects.link(temp_camera)

    # Position camera above the object, looking down -Y axis (Blender front view)
    temp_camera.location = (center_x, center_y - 10, center_z)
    temp_camera.rotation_euler = (math.radians(90), 0, 0)

    # Store original camera
    original_camera = scene.camera

    # Set as active camera
    scene.camera = temp_camera

    return temp_camera, original_camera


def cleanup_gp_render_camera(scene, temp_camera, original_camera):
    """Remove temporary camera and restore original."""
    if temp_camera:
        # Get the camera data before unlinking
        cam_data = temp_camera.data

        # Unlink and remove the object
        scene.collection.objects.unlink(temp_camera)
        bpy.data.objects.remove(temp_camera)

        # Remove the camera data
        if cam_data:
            bpy.data.cameras.remove(cam_data)

    # Restore original camera
    scene.camera = original_camera


def render_grease_pencil_to_png(scene, gp_obj, output_path, scale_factor, resolution_multiplier=1.0, use_workbench=False):
    """
    Render a Grease Pencil object to a PNG file with transparency.
    Returns True on success, False on failure.

    Args:
        use_workbench: If True, uses Workbench engine for crispest possible rendering.
                       If False, uses Eevee with TAA disabled for sharp strokes.
    """
    # Store original settings
    original_render_filepath = scene.render.filepath
    original_file_format = scene.render.image_settings.file_format
    original_color_mode = scene.render.image_settings.color_mode
    original_film_transparent = scene.render.film_transparent
    original_resolution_x = scene.render.resolution_x
    original_resolution_y = scene.render.resolution_y
    original_resolution_percentage = scene.render.resolution_percentage
    original_render_engine = scene.render.engine

    # Store Eevee anti-aliasing settings
    original_taa_samples = scene.eevee.taa_render_samples
    original_taa_reprojection = scene.eevee.use_taa_reprojection

    # Store Workbench settings (in case we switch to it)
    original_shading_light = scene.display.shading.light
    original_shading_color_type = scene.display.shading.color_type

    # Store hide_render state for all objects
    original_hide_render = {}
    for obj in scene.objects:
        original_hide_render[obj.name] = obj.hide_render

    try:
        # Setup temporary camera
        temp_camera, original_camera = setup_gp_render_camera(scene, gp_obj)
        if not temp_camera:
            print(f"Failed to setup camera for {gp_obj.name}")
            return False

        # Hide all objects except the target GP object
        for obj in scene.objects:
            obj.hide_render = (obj != gp_obj and obj != temp_camera)

        # Configure render settings
        scene.render.filepath = output_path
        scene.render.image_settings.file_format = 'PNG'
        scene.render.image_settings.color_mode = 'RGBA'
        scene.render.film_transparent = True

        # Configure render engine for sharp GP strokes
        if use_workbench:
            # Workbench engine for crispest possible rendering (no post-processing)
            scene.render.engine = 'BLENDER_WORKBENCH'
            scene.display.shading.light = 'FLAT'
            scene.display.shading.color_type = 'MATERIAL'
        else:
            # Keep current engine but disable anti-aliasing for sharp strokes
            # (works with Eevee/Eevee Next - the default render engine)
            scene.eevee.taa_render_samples = 1  # Minimum samples (no averaging)
            scene.eevee.use_taa_reprojection = False  # No temporal reprojection

        # Calculate resolution from GP dimensions
        bounds = get_gp_bounds(gp_obj)
        if bounds:
            min_x, _, min_z, max_x, _, max_z = bounds
            width = max_x - min_x
            height = max_z - min_z

            # Resolution = dimensions * scale_factor * resolution_multiplier
            res_x = int(width * scale_factor * resolution_multiplier)
            res_y = int(height * scale_factor * resolution_multiplier)

            # Ensure minimum resolution
            res_x = max(res_x, 1)
            res_y = max(res_y, 1)

            scene.render.resolution_x = res_x
            scene.render.resolution_y = res_y
            scene.render.resolution_percentage = 100

        # Render
        bpy.ops.render.render(write_still=True)

        return True

    except Exception as e:
        print(f"Error rendering GP object {gp_obj.name}: {e}")
        return False

    finally:
        # Cleanup camera
        cleanup_gp_render_camera(scene, temp_camera, original_camera)

        # Restore hide_render state
        for obj in scene.objects:
            if obj.name in original_hide_render:
                obj.hide_render = original_hide_render[obj.name]

        # Restore original render settings
        scene.render.filepath = original_render_filepath
        scene.render.image_settings.file_format = original_file_format
        scene.render.image_settings.color_mode = original_color_mode
        scene.render.film_transparent = original_film_transparent
        scene.render.resolution_x = original_resolution_x
        scene.render.resolution_y = original_resolution_y
        scene.render.resolution_percentage = original_resolution_percentage
        scene.render.engine = original_render_engine

        # Restore Eevee settings
        scene.eevee.taa_render_samples = original_taa_samples
        scene.eevee.use_taa_reprojection = original_taa_reprojection

        # Restore Workbench settings
        scene.display.shading.light = original_shading_light
        scene.display.shading.color_type = original_shading_color_type


def get_gp_object_data(obj, scale_factor, texture_path):
    """
    Extract Grease Pencil object data for export.

    Coordinate mapping:
    - Blender X -> Game X
    - Blender Z -> Game Y (Z-up to Y-down)
    """
    # Get local-space location (equals world for root objects)
    location = obj.matrix_local.to_translation()

    # Get scale from object
    scale = obj.scale

    # Get rotation in Euler angles
    rotation = obj.matrix_local.to_euler('XYZ')

    # Get bounding box dimensions
    bounds = get_gp_bounds(obj)
    if bounds:
        min_x, _, min_z, max_x, _, max_z = bounds
        width = max_x - min_x
        height = max_z - min_z
    else:
        width = 1
        height = 1

    # Map coordinates: X stays X, Z becomes Y (negated for Y-down)
    position = {
        "x": round(location.x * scale_factor, 4),
        "y": -round(location.z * scale_factor, 4)
    }

    # Map scale: X stays X, Z becomes Y
    scale_data = {
        "x": round(scale.x, 4),
        "y": round(scale.z, 4)
    }

    # Dimensions in game units
    dimensions_data = {
        "x": round(width * scale_factor, 4),
        "y": round(height * scale_factor, 4)
    }

    # Use Y rotation, convert from radians to degrees
    rotation_degrees = round(math.degrees(rotation.y), 4)

    # Get origin offset
    origin_offset = get_gp_origin_offset(obj)

    # Get custom properties
    custom_props = get_custom_properties(obj)

    # Get collection membership
    collections = get_object_collections(obj)

    # Get custom properties from each collection
    collection_properties = {}
    for col in obj.users_collection:
        if col.name != "Scene Collection":
            collection_properties[col.name] = get_collection_properties(col)

    # Build UV mapping with full texture coverage
    uv_data = {
        "texturePath": texture_path,
        "uvLayers": [{
            "name": "UVMap",
            "uvCoordinates": [
                {"vertexIndex": 0, "u": 0.0, "v": 0.0},
                {"vertexIndex": 1, "u": 1.0, "v": 0.0},
                {"vertexIndex": 2, "u": 0.0, "v": 1.0},
                {"vertexIndex": 3, "u": 1.0, "v": 1.0}
            ]
        }]
    }

    return {
        "name": obj.name,
        "type": "GREASEPENCIL",
        "parent": obj.parent.name if obj.parent else None,
        "collections": collections,
        "collectionProperties": collection_properties,
        "meshType": "plane",
        "position": position,
        "dimensions": dimensions_data,
        "scale": scale_data,
        "rotation": rotation_degrees,
        "originOffset": origin_offset,
        "customProperties": custom_props,
        "uvMapping": uv_data
    }


def get_custom_properties(obj):
    """Extract custom properties from a Blender object."""
    custom_props = {}

    # Iterate through custom properties (skip internal Blender properties)
    for key in obj.keys():
        if key.startswith("_"):
            continue

        value = obj[key]

        # Convert to JSON-serializable types
        if isinstance(value, (int, float, str, bool)):
            custom_props[key] = value
        elif hasattr(value, "to_list"):
            custom_props[key] = value.to_list()
        else:
            # Try to convert to a basic type
            try:
                custom_props[key] = float(value)
            except (TypeError, ValueError):
                try:
                    custom_props[key] = str(value)
                except:
                    pass  # Skip properties that can't be serialized

    return custom_props


def get_object_collections(obj):
    """
    Get list of collection names the object belongs to.
    Excludes the default "Scene Collection".
    """
    return [col.name for col in obj.users_collection if col.name != "Scene Collection"]


def get_collection_properties(collection):
    """
    Extract custom properties from a Blender collection.
    Handles: float, int, bool, string, and their array variants (Vector).
    """
    props = {}

    for key in collection.keys():
        if key.startswith("_"):
            continue

        value = collection[key]

        # Convert to JSON-serializable types
        if isinstance(value, (int, float, str, bool)):
            props[key] = value
        elif hasattr(value, "to_list"):
            # Handle Vector, Color, and other array-like types
            props[key] = value.to_list()
        else:
            # Try to convert to a basic type
            try:
                props[key] = float(value)
            except (TypeError, ValueError):
                try:
                    props[key] = str(value)
                except:
                    pass  # Skip properties that can't be serialized

    return props


def get_object_data(obj, scale_factor):
    """
    Extract object data for export.

    Coordinate mapping:
    - Blender X -> Game X
    - Blender Z -> Game Y (Z-up to Y-down)
    - Blender Y is discarded (depth in 3D, not used in 2D)
    """
    # Get local-space location (equals world for root objects)
    location = obj.matrix_local.to_translation()

    # Get scale from object (not matrix, to preserve negative scales correctly)
    scale = obj.scale

    # Get rotation in Euler angles
    rotation = obj.matrix_local.to_euler('XYZ')

    # Map coordinates: X stays X, Z becomes Y
    position = {
        "x": round(location.x * scale_factor, 4),
        "y": -round(location.z * scale_factor, 4)
    }

    # Map scale: X stays X, Z becomes Y
    scale_data = {
        "x": round(scale.x, 4),
        "y": round(scale.z, 4)
    }

    # Get dimensions (bounding box size, includes scale)
    dimensions = obj.dimensions
    dimensions_data = {
        "x": round(dimensions.x * scale_factor, 4),
        "y": round(dimensions.y * scale_factor, 4)
    }

    # Use Y rotation
    # Convert from radians to degrees
    rotation_degrees = round(math.degrees(rotation.y), 4)

    # Determine object type
    obj_type = obj.type  # MESH, CAMERA, LIGHT, EMPTY, etc.

    # Get mesh subtype if applicable
    mesh_type = get_mesh_type(obj) if obj.type == 'MESH' else None

    # Get UV mapping data if applicable
    uv_data = get_uv_data(obj) if obj.type == 'MESH' else None

    # Get custom properties
    custom_props = get_custom_properties(obj)

    # For cameras, add orthographic scale as zoom
    if obj.type == 'CAMERA' and obj.data:
        custom_props["zoom"] = round(obj.data.ortho_scale, 4)

    # Get origin offset for meshes
    origin_offset = get_origin_offset(obj) if obj.type == 'MESH' else {"x": 0.5, "y": 0.5}

    # Get collection membership
    collections = get_object_collections(obj)

    # Get custom properties from each collection
    collection_properties = {}
    for col in obj.users_collection:
        if col.name != "Scene Collection":
            collection_properties[col.name] = get_collection_properties(col)

    # Get mesh vertices for collider shapes
    vertices = get_mesh_vertices(obj, scale_factor) if obj.type == 'MESH' else None

    result = {
        "name": obj.name,
        "type": obj_type,
        "parent": obj.parent.name if obj.parent else None,
        "collections": collections,
        "collectionProperties": collection_properties,
        "meshType": mesh_type,
        "position": position,
        "dimensions": dimensions_data,
        "scale": scale_data,
        "rotation": rotation_degrees,
        "originOffset": origin_offset,
        "customProperties": custom_props,
        "vertices": vertices
    }

    # Only include uvMapping if the mesh has UV data
    if uv_data:
        result["uvMapping"] = uv_data

    return result


def get_collection_hierarchy(scene):
    """
    Walk the scene collection tree and build a flat map of collection parents.
    Root collections (direct children of Scene Collection) have None parent.
    Returns: { "NPC": "Characters", "Player": "Characters", "Characters": None }
    """
    hierarchy = {}

    def walk(collection, parent_name):
        for child in collection.children:
            hierarchy[child.name] = parent_name
            walk(child, child.name)

    walk(scene.collection, None)
    return hierarchy


class EXPORT_OT_monodreams_level(bpy.types.Operator, ExportHelper):
    """Export scene objects to MonoDreams JSON level format"""
    bl_idname = "export_scene.monodreams_level"
    bl_label = "Export MonoDreams Level"
    bl_options = {'PRESET', 'UNDO'}

    # File extension
    filename_ext = ".json"

    filter_glob: StringProperty(
        default="*.json",
        options={'HIDDEN'},
        maxlen=255,
    )

    # Export options
    scale_factor: FloatProperty(
        name="Scale Factor",
        description="Blender units to game pixels multiplier",
        default=16.0,
        min=0.001,
        max=10000.0,
    )

    export_selected_only: BoolProperty(
        name="Selected Only",
        description="Export only selected objects",
        default=False,
    )

    # Grease Pencil export options
    export_grease_pencil: BoolProperty(
        name="Export Grease Pencil",
        description="Render Grease Pencil objects to PNG and include in export",
        default=True,
    )

    gp_resolution_multiplier: FloatProperty(
        name="GP Resolution Multiplier",
        description="Resolution scaling for rendered Grease Pencil PNGs",
        default=10.0,
        min=0.1,
        max=100.0,
    )

    gp_output_folder: StringProperty(
        name="GP Output Folder",
        description="Subfolder name for Grease Pencil PNG output",
        default="GreasePencil",
    )

    gp_use_workbench: BoolProperty(
        name="Use Workbench (Crispest)",
        description="Use Workbench engine for sharpest possible GP rendering (no material effects)",
        default=False,
    )

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        layout.prop(self, "scale_factor")
        layout.prop(self, "export_selected_only")

        # Grease Pencil section
        layout.separator()
        layout.label(text="Grease Pencil:")
        layout.prop(self, "export_grease_pencil")
        col = layout.column()
        col.enabled = self.export_grease_pencil
        col.prop(self, "gp_resolution_multiplier")
        col.prop(self, "gp_output_folder")
        col.prop(self, "gp_use_workbench")

    def execute(self, context):
        return self.export_level(context)

    def export_level(self, context):
        # Get objects to export
        if self.export_selected_only:
            objects = context.selected_objects
        else:
            objects = context.scene.objects

        # Filter to exportable types (include GREASEPENCIL)
        exportable_types = {'MESH', 'CAMERA', 'LIGHT', 'EMPTY', 'GREASEPENCIL'}
        objects_to_export = [obj for obj in objects if obj.type in exportable_types]

        # Separate GP objects from regular objects
        gp_objects = [obj for obj in objects_to_export if obj.type == 'GREASEPENCIL']
        regular_objects = [obj for obj in objects_to_export if obj.type != 'GREASEPENCIL']

        # Build collection hierarchy for the scene
        collection_hierarchy = get_collection_hierarchy(context.scene)

        # Build export data
        export_data = {
            "version": "1.0",
            "exportedFrom": f"Blender {bpy.app.version_string}",
            "scaleFactor": self.scale_factor,
            "collectionHierarchy": collection_hierarchy,
            "objects": []
        }

        # Export regular objects
        for obj in regular_objects:
            obj_data = get_object_data(obj, self.scale_factor)
            export_data["objects"].append(obj_data)

        # Export Grease Pencil objects
        if self.export_grease_pencil and gp_objects:
            # Get JSON file directory
            json_dir = os.path.dirname(self.filepath)

            # Create output folder for GP PNGs
            gp_output_dir = os.path.join(json_dir, self.gp_output_folder)
            os.makedirs(gp_output_dir, exist_ok=True)

            gp_exported_count = 0
            for gp_obj in gp_objects:
                # Generate safe filename from object name
                safe_name = "".join(c if c.isalnum() or c in ('_', '-') else '_' for c in gp_obj.name)
                png_filename = f"{safe_name}.png"
                png_path = os.path.join(gp_output_dir, png_filename)

                # Render GP to PNG
                success = render_grease_pencil_to_png(
                    context.scene,
                    gp_obj,
                    png_path,
                    self.scale_factor,
                    self.gp_resolution_multiplier,
                    self.gp_use_workbench
                )

                if success:
                    # Build texture path for JSON (relative path that works with Content pipeline)
                    texture_path = os.path.join(gp_output_dir, png_filename)

                    # Get GP object data and add to export
                    gp_data = get_gp_object_data(gp_obj, self.scale_factor, texture_path)
                    export_data["objects"].append(gp_data)
                    gp_exported_count += 1
                else:
                    self.report({'WARNING'}, f"Failed to render Grease Pencil object: {gp_obj.name}")

            if gp_exported_count > 0:
                self.report({'INFO'}, f"Rendered {gp_exported_count} Grease Pencil objects to {gp_output_dir}")

        # Sort objects by name for consistent output
        export_data["objects"].sort(key=lambda x: x["name"])

        # Write JSON file
        try:
            with open(self.filepath, 'w', encoding='utf-8') as f:
                json.dump(export_data, f, indent=2, ensure_ascii=False)

            self.report({'INFO'}, f"Exported {len(export_data['objects'])} objects to {self.filepath}")
            return {'FINISHED'}

        except Exception as e:
            self.report({'ERROR'}, f"Failed to export: {str(e)}")
            return {'CANCELLED'}


def menu_func_export(self, context):
    self.layout.operator(EXPORT_OT_monodreams_level.bl_idname, text="MonoDreams Level (.json)")


def register():
    bpy.utils.register_class(EXPORT_OT_monodreams_level)
    bpy.types.TOPBAR_MT_file_export.append(menu_func_export)


def unregister():
    bpy.types.TOPBAR_MT_file_export.remove(menu_func_export)
    bpy.utils.unregister_class(EXPORT_OT_monodreams_level)


if __name__ == "__main__":
    register()
