
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using System;
using System.Collections.Generic;
using AcDb = Autodesk.AutoCAD.DatabaseServices;


namespace PipeCatchBasin
{
    public class PipeCommands
    {
        [CommandMethod("PipeDepthAndSlope", CommandFlags.UsePickSet)]
        public static void PipeDepthAndSlope()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            //Try PickFirst
            PromptSelectionResult psr1 = ed.SelectImplied();
            ed.SetImpliedSelection(new ObjectId[0]);
            SelectionSet ss = null;

            if (psr1.Status == PromptStatus.OK && psr1.Value.Count > 0)
            {
                // Filter pickfirst selection to only pipes
                var validPipeIds = new List<AcDb.ObjectId>();

                foreach (SelectedObject obj in psr1.Value)
                {
                    if (obj != null && obj.ObjectId.ObjectClass.DxfName == "AECC_PIPE")
                    {
                        validPipeIds.Add(obj.ObjectId);
                    }
                }

                if (validPipeIds.Count > 0)
                {
                    ss = SelectionSet.FromObjectIds(validPipeIds.ToArray());

                }
            }
            else
            {
                //Prompt user for selection if Pickfirst is empty
                SelectionFilter filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "AECC_PIPE")
                });

                psr1 = ed.GetSelection(filter);
                if (psr1.Status != PromptStatus.OK)
                    return;

                ss = psr1.Value;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {


                //Prompt for depth
                PromptDoubleOptions pdo = new PromptDoubleOptions("\nEnter depth below Top Level (m): ");
                pdo.AllowNegative = false;
                pdo.AllowZero = false;
                pdo.DefaultValue = 1.20;
                PromptDoubleResult pdr = ed.GetDouble(pdo);
                if (pdr.Status != PromptStatus.OK) return;
                ////////////////////////////////////////////////
                //Prompt for slope
                PromptDoubleOptions pso = new PromptDoubleOptions("\nEnter slope(%): ");
                pso.AllowZero = false;
                pso.DefaultValue = 2;
                PromptDoubleResult psr = ed.GetDouble(pso);
                if (psr.Status != PromptStatus.OK) return;
                double pipeStartSlope = -Math.Abs(psr.Value) / 100;
                ///////////////////////////////////////////////
                // Prompt once for sump override
                PromptKeywordOptions sso = new PromptKeywordOptions("\nSet Sump depth to zero?")
                {
                    AllowNone = false
                };
                sso.Keywords.Add("Yes");
                sso.Keywords.Add("No");
                sso.Keywords.Default = "Yes";
                PromptResult ssr = ed.GetKeywords(sso);
                if (ssr.Status != PromptStatus.OK) return;
                bool resetSump = ssr.StringResult == "Yes";
                ///////////////////////////////////////////////
                foreach (SelectedObject selObj in ss)
                {
                    //Create pipe object
                    if (selObj == null)
                        continue;
                    Pipe pipe = tr.GetObject(selObj.ObjectId, OpenMode.ForWrite) as Pipe;
                    if (pipe == null)
                        continue;

                    //Get top of structre
                    if (pipe.StartStructureId == ObjectId.Null)
                    {
                        ed.WriteMessage($"\n⚠ Pipe {pipe.Handle} has no start structure. Skipping.");
                        continue;
                    }
                    Structure startStruct = tr.GetObject(pipe.StartStructureId, OpenMode.ForWrite) as Structure;
                    if (startStruct == null) continue;

                    double structTopLevel = startStruct.RimElevation;
                    double pipeStartInvert = structTopLevel - pdr.Value;

                    //set invert level
                    Point3d currentStart = pipe.StartPoint;
                    pipe.StartPoint = new Point3d(currentStart.X, currentStart.Y, pipeStartInvert + pipe.InnerDiameterOrWidth / 2);
                    //set slope
                    pipe.SetSlopeHoldStart(pipeStartSlope);
                    ed.WriteMessage($"\n✔ Pipe start invert: {pipeStartInvert:F2}");
                    ed.WriteMessage($"\n✔ Pipe slope: {pipeStartSlope * -100:F2}%");
                    // Optionally override sump
                    if (resetSump)
                    {
                        startStruct.SumpElevation = pipeStartInvert;
                        ed.WriteMessage($"\n✔ Structure {startStruct.Handle} sump depth set to 0");
                    }
                    else
                    {
                        startStruct.SumpElevation = pipeStartInvert +startStruct.SumpDepth;
                        ed.WriteMessage($"\n✔ Structure {startStruct.Handle} sump depth unchanged");
                    }
                    ed.WriteMessage($"\n✔ Pipe {pipe.Handle} adjusted. Invert: {pipeStartInvert:F2}, Slope: {-pipeStartSlope * 100:F2}%");
                }
                tr.Commit();
            }
            }
        }
    }

