using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using g3;

namespace gsMeshBoolean
{
    class gsMeshBoolean
    {

        static void print_usage()
        {
            System.Console.WriteLine("gsMeshBoolean v1.0 - Copyright gradientspace / Ryan Schmidt / RoleModel Software 2019");
            System.Console.WriteLine("Questions? Comments? www.gradientspace.com or @gradientspace");
            System.Console.WriteLine("usage: gsMeshBoolean options <inputmesh> <unionmesh>");
            System.Console.WriteLine("options:");
            System.Console.WriteLine("  -operation <mode>   : union, intersection, or difference");
            System.Console.WriteLine("  -detail <divisions> : sampling rate for the geometry");
            System.Console.WriteLine("  -output <filename>  : output filename - default is inputmesh.reduced.fmt");
            System.Console.WriteLine("  -v                  : verbose ");
        }

        static void Main(string[] args)
        {
            CommandArgumentSet arguments = new CommandArgumentSet();
            //arguments.Register("-tcount", int.MaxValue);
            arguments.Register("-operation", "");
            arguments.Register("-detail", 128);
            //arguments.Register("-percent", 50.0f);
            arguments.Register("-v", false);
            arguments.Register("-output", "");
            if (arguments.Parse(args) == false) {
                return;
            }

            if (arguments.Filenames.Count != 2) {
                print_usage();
                return;
            }
            string inputFilename = arguments.Filenames[0];
            if (! File.Exists(inputFilename) ) {
                System.Console.WriteLine("File {0} does not exist", inputFilename);
                return;
            }

            string inputFilename2 = arguments.Filenames[1];
            if (!File.Exists(inputFilename))
            {
                System.Console.WriteLine("File {0} does not exist", inputFilename2);
                return;
            }

            string outputFilename = Path.GetFileNameWithoutExtension(inputFilename);
            string format = Path.GetExtension(inputFilename);
            outputFilename = outputFilename + ".operated" + format;
            if (arguments.Saw("-output")) {
                outputFilename = arguments.Strings["-output"];
            }

            string operationMode = "union";
            if ( arguments.Saw("-operation")) {
                operationMode = arguments.Strings["-operation"];
            }

            int granularity = 128;
            if ( arguments.Saw("-detail"))
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

            try
            {
                DMesh3Builder builder = new DMesh3Builder();
                IOReadResult result = StandardMeshReader.ReadFile(inputFilename2, ReadOptions.Defaults, builder);
                if (result.code != IOCode.Ok)
                {
                    System.Console.WriteLine("Error reading {0} : {1}", inputFilename2, result.message);
                    return;
                }
                meshes.AddRange(builder.Meshes);
            }
            catch (Exception e)
            {
                System.Console.WriteLine("Exception reading {0} : {1}", inputFilename, e.Message);
                return;
            }

            DMesh3 mesh = meshes[0];
            for (int k = 1; k < meshes.Count; ++k)
                MeshEditor.Append(mesh, meshes[k]);
            if ( mesh.TriangleCount == 0 ) {
                System.Console.WriteLine("mesh does not contain any triangles");
                //return;
            }

            int originalTriangleCount = meshes[0].TriangleCount + meshes[1].TriangleCount;

            if (verbose)
                System.Console.WriteLine("...completed loading.");
                System.Console.WriteLine("triangles: {0}", originalTriangleCount);

            // repair meshes

            DMesh3 repairMesh(DMesh3 inputMesh)
            {
                gs.MeshAutoRepair repair = new gs.MeshAutoRepair(inputMesh);
                repair.Apply();
                return inputMesh;
            }

            DMesh3 meshA = repairMesh(meshes[0]);
            DMesh3 meshB = repairMesh(meshes[1]);
            int postRepairTriangleCount = meshA.TriangleCount + meshB.TriangleCount;

            if (verbose)
                System.Console.WriteLine("...completed repair.");
                System.Console.WriteLine("triangles: {0}", postRepairTriangleCount);

            // start

            BoundedImplicitFunction3d generateIso(DMesh3 inputMesh)
            {
                int num_cells = granularity;
                double cell_size = inputMesh.CachedBounds.MaxDim / num_cells;

                MeshSignedDistanceGrid sdf = new MeshSignedDistanceGrid(inputMesh, cell_size);
                sdf.Compute();

                return new DenseGridTrilinearImplicit(sdf.Grid, sdf.GridOrigin, sdf.CellSize);
            }

            BoundedImplicitFunction3d isoA = generateIso(meshA);
            BoundedImplicitFunction3d isoB = generateIso(meshB);


            BoundedImplicitFunction3d isoOperator = null;
            if (operationMode == "union")
            {
                isoOperator = new ImplicitUnion3d
                {
                    A = isoA,
                    B = isoB
                };
            } else if (operationMode == "intersection")
            {
                isoOperator = new ImplicitIntersection3d
                {
                    A = isoA,
                    B = isoB
                };
            } else if (operationMode == "difference")
            {
                isoOperator = new ImplicitDifference3d
                {
                    A = isoA,
                    B = isoB
                };
            };


            AxisAlignedBox3d box = meshes[0].CachedBounds;
            AxisAlignedBox3d box2 = meshes[1].CachedBounds;

            AxisAlignedBox3d maximumBounds = new AxisAlignedBox3d(
                Math.Min(box.Min.x, box2.Min.x), Math.Min(box.Min.y, box2.Min.y), Math.Min(box.Min.z, box2.Min.z),
                Math.Max(box.Max.x, box2.Max.x), Math.Max(box.Max.y, box2.Max.y), Math.Max(box.Max.z, box2.Max.z)
            );

            MarchingCubes c = new MarchingCubes();
            c.Implicit = isoOperator;
            c.Bounds = maximumBounds;
            c.CubeSize = c.Bounds.MaxDim / granularity;
            c.Bounds.Expand(3 * c.CubeSize);

            c.Generate();
            DMesh3 outputMesh = c.Mesh;

            // end
            System.Console.WriteLine("...completed remeshing.");
            System.Console.WriteLine("triangles: {0}", outputMesh.TriangleCount);
            Reducer r = new Reducer(outputMesh);
            //r.ReduceToTriangleCount(originalTriangleCount);
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
