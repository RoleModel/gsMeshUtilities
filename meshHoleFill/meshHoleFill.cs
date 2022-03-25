using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using g3;
using gs;
using System.Json;

namespace meshHoleFill
{
    class meshHoleFill
    {

        static void print_usage()
        {
            System.Console.WriteLine("meshHoleFill v1.0 - Copyright gradientspace / Ryan Schmidt / RoleModel Software 2020");
            System.Console.WriteLine("Questions? Comments? www.gradientspace.com or @gradientspace");
            System.Console.WriteLine("usage: meshHoleFill options -selection <json> <inputmesh>");
            System.Console.WriteLine("options:");
            System.Console.WriteLine("  -detail <length>       : desired edge length for geometry fill");
            System.Console.WriteLine("  -selection <filename>  : JSON array of face indexes to delete");
            System.Console.WriteLine("  -displacement <string> : CSV floats representing displacement vector premultiplied");
            System.Console.WriteLine("  -output <filename>     : output filename - default is inputmesh.reduced.fmt");
            System.Console.WriteLine("  -v                     : verbose ");
        }

        static void Main(string[] args)
        {
            CommandArgumentSet arguments = new CommandArgumentSet();
            arguments.Register("-v", false);
            arguments.Register("-detail", 0.5f);
            arguments.Register("-selection", "");
            arguments.Register("-displacement", "");
            arguments.Register("-output", "");
            if (arguments.Parse(args) == false) {
                return;
            }

            if (arguments.Filenames.Count != 1)
            {
                print_usage();
                return;
            }
            string inputFilename = arguments.Filenames[0];
            if (!File.Exists(inputFilename))
            {
                System.Console.WriteLine("File {0} does not exist", inputFilename);
                return;
            }

            string outputFilename = Path.GetFileNameWithoutExtension(inputFilename);
            string format = Path.GetExtension(inputFilename);
            outputFilename = outputFilename + ".operated" + format;
            if (arguments.Saw("-output")) {
                outputFilename = arguments.Strings["-output"];
            }

            int[] selectionIndexes = { };
            if (arguments.Saw("-selection"))
            {
                string indexString = System.IO.File.ReadAllText(arguments.Strings["-selection"]);

                JsonValue json = JsonValue.Parse(indexString);
                selectionIndexes = new int[json.Count];

                for (var i = 0; i < json.Count; i++)
                {
                    int value = json[i];
                    selectionIndexes[i] = value;
                }
            }

            if(selectionIndexes.Length == 0)
            {
                System.Console.WriteLine("Selection required");
                print_usage();
                return;
            }

            double offsetDistance = 1.0;
            Vector3d offsetDirection = Vector3d.Zero;
            if (arguments.Saw("-displacement"))
            {
                string[] displacementStrings = arguments.Strings["-displacement"].Split(new[]{','});

                offsetDirection.x = Double.Parse(displacementStrings[0]);
                offsetDirection.y = Double.Parse(displacementStrings[1]);
                offsetDirection.z = Double.Parse(displacementStrings[2]);
            }

            float meshResolution = 0.5f;
            if (arguments.Saw("-detail"))
            {
                meshResolution = arguments.Floats["-detail"];
            }

            bool verbose = false;
            if (arguments.Saw("-v"))
                verbose = arguments.Flags["-v"];

            List<DMesh3> meshes;
            try {
                DMesh3Builder builder = new DMesh3Builder();
                IOReadResult result = StandardMeshReader.ReadFile(inputFilename, ReadOptions.Defaults, builder);
                if (result.code != IOCode.Ok) {
                    System.Console.WriteLine("Error reading {0} : {1}", inputFilename, result.message);
                    return;
                }
                meshes = builder.Meshes;
            } catch (Exception e) {
                System.Console.WriteLine("Exception reading {0} : {1}", inputFilename, e.Message);
                return;
            }
            if (meshes.Count == 0) {
                System.Console.WriteLine("file did not contain any valid meshes");
                return;
            }

            DMesh3 mesh = meshes[0];
            for (int k = 1; k < meshes.Count; ++k)
                MeshEditor.Append(mesh, meshes[k]);
            if (mesh.TriangleCount == 0) {
                System.Console.WriteLine("mesh does not contain any triangles");
                return;
            }

            if (verbose)
                System.Console.WriteLine("...completed loading.");
            System.Console.WriteLine("triangles: {0}", mesh.TriangleCount);

            DMesh3 meshA = meshes[0];

            if (verbose)
                System.Console.WriteLine("...completed repair.");
                System.Console.WriteLine("triangles: {0}", meshA.TriangleCount);

            // start
            if (verbose)
                System.Console.WriteLine("selected: {0}", selectionIndexes.Length);

            MeshRepairOrientation orient = new MeshRepairOrientation(meshA);
            orient.OrientComponents();

            MeshRegionBoundaryLoops loops = new MeshRegionBoundaryLoops(meshA, selectionIndexes);

            MeshEditor editor = new MeshEditor(meshA);
            editor.RemoveTriangles(selectionIndexes, false);

            System.Console.WriteLine("triangles: {0}", meshA.TriangleCount);

            foreach (EdgeLoop loop in loops)
            {
                SmoothedHoleFill filler = new SmoothedHoleFill(meshA, loop);
                filler.TargetEdgeLength = meshResolution;
                filler.ConstrainToHoleInterior = true;
                filler.SmoothSolveIterations = 3;
                filler.OffsetDirection = offsetDirection;
                filler.OffsetDistance = offsetDistance;
                filler.Apply();
            }

            orient = new MeshRepairOrientation(meshA);
            orient.OrientComponents();

            System.Console.WriteLine("triangles: {0}", meshA.TriangleCount);

            if (verbose)
                System.Console.WriteLine("done!");

            try {
                WriteOptions options = WriteOptions.Defaults;
                options.bWriteBinary = true;
                IOWriteResult wresult =
                    StandardMeshWriter.WriteMesh(outputFilename, meshA, options);
                if (wresult.code != IOCode.Ok) {
                    System.Console.WriteLine("Error writing {0} : {1}", inputFilename, wresult.message);
                    return;
                }
            } catch (Exception e) {
                System.Console.WriteLine("Exception reading {0} : {1}", inputFilename, e.Message);
                return;
            }

            return;
        }
    }
}
