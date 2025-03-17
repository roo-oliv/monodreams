using System.Globalization;
using Microsoft.Xna.Framework.Content.Pipeline;
using Yarn.Compiler;

namespace MonoDreams.YarnSpinner;

[ContentProcessor(DisplayName = "YarnSpinner Processor - MonoDreams")]
public class YarnSpinnerProcessor : ContentProcessor<YarnSpinnerFile, YarnProgram>
{
    public override YarnProgram Process(YarnSpinnerFile input, ContentProcessorContext context)
    {
        string baseLanguageId = CultureInfo.CurrentCulture.Name;

        try
        {
            context.Logger.LogMessage("Compiling Yarn program {0}", input.FileName);
            context.Logger.LogMessage("File content: {0}", input.Text.Substring(0, Math.Min(input.Text.Length, 100)) + "...");

            var compilationJob = CompilationJob.CreateFromString(input.FileName, input.Text, null);
            context.Logger.LogMessage("Compilation job created with file: {0}", input.FileName);
            
            var compilationResult = Compiler.Compile(compilationJob);
            context.Logger.LogMessage("Compilation completed. Has program: {0}", compilationResult.Program != null);
            
            if (compilationResult.Program == null)
            {
                context.Logger.LogMessage("Yarn compilation failed.");
                foreach (var diagnostic in compilationResult.Diagnostics)
                {
                    context.Logger.LogMessage("Diagnostic: {0}", diagnostic.ToString());
                }
                // Return an empty program if compilation failed
                return new YarnProgram { CompiledProgram = new byte[0] };
            }
            
            // Fix the formatting of the Yarn script if needed
            if (compilationResult.Program.Nodes.Count == 0)
            {
                context.Logger.LogMessage("No nodes found, attempting to fix the script format...");
                
                // Try to extract nodes using a simpler approach
                var fixedText = FixYarnFormat(input.Text);
                if (fixedText != input.Text)
                {
                    context.Logger.LogMessage("Fixed script format. Recompiling...");
                    compilationJob = CompilationJob.CreateFromString(input.FileName, fixedText, null);
                    compilationResult = Compiler.Compile(compilationJob);
                    
                    if (compilationResult.Program == null)
                    {
                        context.Logger.LogMessage("Recompilation failed after format fix.");
                        return new YarnProgram { CompiledProgram = new byte[0] };
                    }
                }
            }
            
            // Log node information
            context.Logger.LogMessage("Program has {0} nodes", compilationResult.Program.Nodes.Count);
            foreach (var node in compilationResult.Program.Nodes)
            {
                context.Logger.LogMessage("Found node: {0}", node.Key);
            }
            
            if (compilationResult.ContainsImplicitStringTags)
            {
                context.Logger.LogMessage("Yarn compilation includes untagged strings.");
            }
            
            var programContainer = new YarnProgram();

            using (var memoryStream = new MemoryStream())
            using (var outputStream = new Google.Protobuf.CodedOutputStream(memoryStream))
            {
                // Serialize the compiled program to memory
                compilationResult.Program.WriteTo(outputStream);
                outputStream.Flush();

                byte[] compiledBytes = memoryStream.ToArray();
                programContainer.CompiledProgram = compiledBytes;
                context.Logger.LogMessage("Serialized program size: {0} bytes", compiledBytes.Length);
            }

            using (var memoryStream = new MemoryStream())
            {
                using (var textWriter = new StreamWriter(memoryStream))
                {
                    // Generate the localized .csv file

                    // Use the invariant culture when writing the CSV
                    var configuration = new CsvHelper.Configuration.Configuration(CultureInfo.InvariantCulture);

                    var csv = new CsvHelper.CsvWriter(textWriter, configuration);
                    var lines = compilationResult.StringTable?.Select(x => new
                    {
                        id = x.Key,
                        text = x.Value.text,
                        file = x.Value.fileName,
                        node = x.Value.nodeName,
                        lineNumber = x.Value.lineNumber
                    }) ?? Enumerable.Empty<object>();

                    csv.WriteRecords(lines);

                    textWriter.Flush();

                    memoryStream.Position = 0;

                    using (var reader = new StreamReader(memoryStream))
                    {
                        string text = reader.ReadToEnd();
                        string textFileName = $"{input.FileName} ({baseLanguageId})";

                        programContainer.BaseLocalizationId = baseLanguageId;
                        programContainer.BaseLocalisationStringTable = text;
                        programContainer.Localizations = new YarnTranslation[0];
                    }
                }
            }

            return programContainer;
        }
        catch (Exception ex)
        {
            context.Logger.LogMessage("Error in YarnSpinnerProcessor: {0}", ex);
            context.Logger.LogMessage("Stack trace: {0}", ex.StackTrace);
            throw;
        }
    }
    
    // Attempt to fix the format of a Yarn script
    private string FixYarnFormat(string text)
    {
        try
        {
            var lines = text.Split('\n');
            var result = new List<string>();
            bool inNode = false;
            bool hasTitle = false;
            bool hasNodeSeparator = false;
            
            // First, check if we have a valid Yarn file structure
            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("title:")) hasTitle = true;
                if (line.Trim() == "---") hasNodeSeparator = true;
            }
            
            // If we don't have a title or node separator, restructure the file
            if (!hasTitle || !hasNodeSeparator)
            {
                Console.WriteLine("Restructuring Yarn file - missing title or node separator");
                
                // Start with a title
                result.Add("title: HelloWorld");
                result.Add("---");
                
                // Add all non-empty lines as dialogue
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        // Skip any existing title or separator lines
                        if (trimmedLine.StartsWith("title:") || trimmedLine == "---" || trimmedLine == "===")
                            continue;
                            
                        result.Add(line);
                    }
                }
                
                // End the node
                result.Add("===");
                
                return string.Join("\n", result);
            }
            
            // Process the file line by line, ensuring proper structure
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmedLine = line.Trim();
                
                // Skip empty lines at the beginning
                if (string.IsNullOrWhiteSpace(trimmedLine) && !inNode && result.Count == 0)
                {
                    continue;
                }
                
                // Handle title lines
                if (trimmedLine.StartsWith("title:"))
                {
                    if (inNode)
                    {
                        // End previous node if needed
                        result.Add("===");
                        result.Add("");
                    }
                    
                    inNode = true;
                    result.Add(line);
                    continue;
                }
                
                // Handle node separator lines
                if (trimmedLine == "---")
                {
                    inNode = true;
                    result.Add(line);
                    continue;
                }
                
                // Handle node end lines
                if (trimmedLine == "===")
                {
                    inNode = false;
                    result.Add(line);
                    result.Add("");  // Add an empty line after each node
                    continue;
                }
                
                // Add the line as is
                result.Add(line);
            }
            
            // Ensure the last node ends properly
            if (inNode)
            {
                result.Add("===");
            }
            
            return string.Join("\n", result);
        }
        catch (Exception ex)
        {
            // In case of errors, log and return the original text
            Console.WriteLine($"Error fixing Yarn format: {ex.Message}");
            return text;
        }
    }
}