# MonoDreams Level Exporter for Blender 5.0.0
# Exports scene objects to JSON format for use with MonoDreams game engine

bl_info = {
    "name": "MonoDreams Level Exporter",
    "author": "MonoDreams Team",
    "version": (1, 1, 1),
    "blender": (5, 0, 0),
    "location": "File > Export > MonoDreams Level (.json)",
    "description": "Export level objects to JSON for MonoDreams game engine",
    "category": "Import-Export",
}

import bpy
import json
import math
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


def get_object_data(obj, scale_factor):
    """
    Extract object data for export.

    Coordinate mapping:
    - Blender X -> Game X
    - Blender Z -> Game Y (Z-up to Y-down)
    - Blender Y is discarded (depth in 3D, not used in 2D)
    """
    # Get world-space location
    location = obj.matrix_world.to_translation()

    # Get scale from object (not matrix, to preserve negative scales correctly)
    scale = obj.scale

    # Get rotation in Euler angles
    rotation = obj.matrix_world.to_euler('XYZ')

    # Map coordinates: X stays X, Z becomes Y
    position = {
        "x": round(location.x * scale_factor, 4),
        "y": round(location.z * scale_factor, 4)
    }

    # Map scale: X stays X, Z becomes Y
    scale_data = {
        "x": round(scale.x, 4),
        "y": round(scale.z, 4)
    }

    # Get dimensions (bounding box size, includes scale)
    # Map dimensions: X stays X, Z becomes Y
    dimensions = obj.dimensions
    dimensions_data = {
        "x": round(dimensions.x * scale_factor, 4),
        "y": round(dimensions.y * scale_factor, 4)
    }

    # Use Z rotation (around vertical axis in Blender = around screen in 2D)
    # Convert from radians to degrees
    rotation_degrees = round(math.degrees(rotation.z), 4)

    # Determine object type
    obj_type = obj.type  # MESH, CAMERA, LIGHT, EMPTY, etc.

    # Get mesh subtype if applicable
    mesh_type = get_mesh_type(obj) if obj.type == 'MESH' else None

    # Get UV mapping data if applicable
    uv_data = get_uv_data(obj) if obj.type == 'MESH' else None

    # Get custom properties
    custom_props = get_custom_properties(obj)

    result = {
        "name": obj.name,
        "type": obj_type,
        "meshType": mesh_type,
        "position": position,
        "dimensions": dimensions_data,
        "scale": scale_data,
        "rotation": rotation_degrees,
        "customProperties": custom_props
    }

    # Only include uvMapping if the mesh has UV data
    if uv_data:
        result["uvMapping"] = uv_data

    return result


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

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        layout.prop(self, "scale_factor")
        layout.prop(self, "export_selected_only")

    def execute(self, context):
        return self.export_level(context)

    def export_level(self, context):
        # Get objects to export
        if self.export_selected_only:
            objects = context.selected_objects
        else:
            objects = context.scene.objects

        # Filter to exportable types
        exportable_types = {'MESH', 'CAMERA', 'LIGHT', 'EMPTY'}
        objects_to_export = [obj for obj in objects if obj.type in exportable_types]

        # Build export data
        export_data = {
            "version": "1.0",
            "exportedFrom": f"Blender {bpy.app.version_string}",
            "scaleFactor": self.scale_factor,
            "objects": []
        }

        for obj in objects_to_export:
            obj_data = get_object_data(obj, self.scale_factor)
            export_data["objects"].append(obj_data)

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
