using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using g3;

namespace meshOffset
{
    class meshOffset
    {

        static void print_usage()
        {
            System.Console.WriteLine("meshOffset v1.0 - Copyright gradientspace / Ryan Schmidt / RoleModel Software 2020");
            System.Console.WriteLine("Questions? Comments? www.gradientspace.com or @gradientspace");
            System.Console.WriteLine("usage: meshOffset options <inputmesh>");
            System.Console.WriteLine("options:");
            System.Console.WriteLine("  -detail <divisions> : sampling rate for the geometry");
            System.Console.WriteLine("  -offset <distance>  : distance to offset");
            System.Console.WriteLine("  -output <filename>  : output filename - default is inputmesh.reduced.fmt");
            System.Console.WriteLine("  -v                  : verbose ");
        }

        static void Main(string[] args)
        {
            CommandArgumentSet arguments = new CommandArgumentSet();
            arguments.Register("-detail", 128);
            arguments.Register("-offset", 2.0f);
            //arguments.Register("-percent", 50.0f);
            arguments.Register("-v", false);
            arguments.Register("-output", "");
            if (arguments.Parse(args) == false) {
                return;
            }

            if (arguments.Filenames.Count != 1) {
                print_usage();
                return;
            }
            string inputFilename = arguments.Filenames[0];
            if (! File.Exists(inputFilename) ) {
                System.Console.WriteLine("File {0} does not exist", inputFilename);
                return;
            }

            string outputFilename = Path.GetFileNameWithoutExtension(inputFilename);
            string format = Path.GetExtension(inputFilename);
            outputFilename = outputFilename + ".operated" + format;
            if (arguments.Saw("-output")) {
                outputFilename = arguments.Strings["-output"];
            }

            float distance = 2.0f;
            if ( arguments.Saw("-offset"))
            {
                distance = arguments.Floats["-offset"];
            }

            int granularity = 128;
            if (arguments.Saw("-detail"))
            {
                granularity = arguments.Integers["-detail"];
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
            if ( meshes.Count == 0 ) {
                System.Console.WriteLine("file did not contain any valid meshes");
                return;
            }

            DMesh3 mesh = meshes[0];
            for (int k = 1; k < meshes.Count; ++k)
                MeshEditor.Append(mesh, meshes[k]);
            if ( mesh.TriangleCount == 0 ) {
                System.Console.WriteLine("mesh does not contain any triangles");
                return;
            }

            if (verbose)
                System.Console.WriteLine("...completed loading.");
                System.Console.WriteLine("triangles: {0}", mesh.TriangleCount);

            // repair meshes

            DMesh3 repairMesh(DMesh3 inputMesh)
            {
                gs.MeshAutoRepair repair = new gs.MeshAutoRepair(inputMesh);
                repair.Apply();
                return inputMesh;
            }

            DMesh3 meshA = repairMesh(meshes[0]);

            if (verbose)
                System.Console.WriteLine("...completed repair.");
                System.Console.WriteLine("triangles: {0}", meshA.TriangleCount);

            // start

            double d = distance;

            BoundedImplicitFunction3d generateIso(DMesh3 inputMesh)
            {
                int num_cells = granularity;
                double cell_size = inputMesh.CachedBounds.MaxDim / num_cells;
                double allowedOffsetMagnitude = Math.Abs(d);

                MeshSignedDistanceGrid sdf = new MeshSignedDistanceGrid(inputMesh, cell_size);
                sdf.ExpandBounds = new Vector3d(allowedOffsetMagnitude);
                sdf.ExactBandWidth = (int)(allowedOffsetMagnitude / cell_size) + 1;
                sdf.Compute();

                return new CachingDenseGridTrilinearImplicit(sdf);
            }

            BoundedImplicitFunction3d isoA = generateIso(meshA);


            BoundedImplicitFunction3d isoOperator = null;
            System.Console.WriteLine("distance: {0}", d);

            isoOperator = new ImplicitOffset3d
            {
                A = isoA,
                Offset = d
            };

            MarchingCubes c = new MarchingCubes();
            c.Implicit = isoOperator;
            c.RootMode = MarchingCubes.RootfindingModes.LerpSteps;
            c.RootModeSteps = 5;                                        // number of iterations
            c.Bounds = isoOperator.Bounds();
            c.CubeSize = c.Bounds.MaxDim / granularity;
            c.Bounds.Expand(3 * c.CubeSize);

            c.Generate();
            MeshNormals.QuickCompute(c.Mesh);
            DMesh3 outputMesh = c.Mesh;

            // end
            System.Console.WriteLine("...completed remeshing.");
            System.Console.WriteLine("triangles: {0}", outputMesh.TriangleCount);
            Reducer r = new Reducer(outputMesh);
            //r.ReduceToTriangleCount(meshA.TriangleCount);
            r.ReduceToEdgeLength(2 * c.CubeSize);
            System.Console.WriteLine("..completed reducing.");
            System.Console.WriteLine("triangles: {0}", outputMesh.TriangleCount);


            if (verbose)
                System.Console.WriteLine("done!");

            try {
                WriteOptions options = WriteOptions.Defaults;
                options.bWriteBinary = true;
                IOWriteResult wresult =
                    StandardMeshWriter.WriteMesh(outputFilename, outputMesh, options);
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
