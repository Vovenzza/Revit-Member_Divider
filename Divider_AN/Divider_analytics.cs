using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Divider_AN
{
    [Transaction(TransactionMode.Manual)]
    public class Divider_analytics : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                Reference beamRef = uidoc.Selection.PickObject(ObjectType.Element, "Select Analytical Member");
                AnalyticalMember beam = doc.GetElement(beamRef) as AnalyticalMember;

                if (beam == null)
                {
                    TaskDialog.Show("Error", "Please select an analytical member.");
                    return Result.Failed;
                }

                Curve beamCurve = (beam.Location as LocationCurve)?.Curve;
                if (beamCurve == null)
                {
                    TaskDialog.Show("Error", "Selected member has no valid curve.");
                    return Result.Failed;
                }

                List<XYZ> points = GetConnectionPoints(doc, beam, beamCurve, doc.Application.ShortCurveTolerance);
                if (points.Count < 2)
                {
                    TaskDialog.Show("Info", "Not enough connection points to split member.");
                    return Result.Failed;
                }

                using (Transaction tx = new Transaction(doc, "Split Analytical Member"))
                {
                    tx.Start();
                    CreateNewSegments(doc, beam, SortPoints(beamCurve, points));
                    doc.Delete(beam.Id);
                    tx.Commit();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private List<XYZ> GetConnectionPoints(Document doc, AnalyticalMember beam, Curve beamCurve, double tolerance)
        {
            List<XYZ> points = new List<XYZ>();

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_AnalyticalMember)
                .WhereElementIsNotElementType();

            foreach (Element elem in collector)
            {
                if (!(elem is AnalyticalMember member) || member.Id == beam.Id) continue;
                if (!(member.Location is LocationCurve location)) continue;

                Curve otherCurve = location.Curve;
                if (FindIntersection(beamCurve, otherCurve, tolerance, out XYZ intersection))
                {
                    points.Add(intersection);
                }
            }

            points.Add(beamCurve.GetEndPoint(0));
            points.Add(beamCurve.GetEndPoint(1));

            return points.Distinct(new XYZComparer(tolerance)).ToList();
        }

        private bool FindIntersection(Curve a, Curve b, double tolerance, out XYZ point)
        {
            point = null;
            if (a.Intersect(b, out IntersectionResultArray results) == SetComparisonResult.Overlap)
            {
                foreach (IntersectionResult result in results)
                {
                    if (result.Distance < tolerance)
                    {
                        point = result.XYZPoint;
                        return true;
                    }
                }
            }
            return false;
        }

        private List<XYZ> SortPoints(Curve curve, List<XYZ> points)
        {
            return points.OrderBy(p => curve.ComputeNormalizedParameter(curve.Project(p).Parameter)).ToList();
        }

        private void CreateNewSegments(Document doc, AnalyticalMember original, List<XYZ> sortedPoints)
        {
            ElementId typeId = GetValidTypeId(original, doc);

            // Use correct enum name for Revit 2023
            AnalyticalDiscipline discipline = AnalyticalDiscipline.Analytical;

            for (int i = 0; i < sortedPoints.Count - 1; i++)
            {
                XYZ start = sortedPoints[i];
                XYZ end = sortedPoints[i + 1];

                if (start.DistanceTo(end) > doc.Application.ShortCurveTolerance)
                {
                    Line segment = Line.CreateBound(start, end);
                    AnalyticalMember.Create(doc, segment, typeId, discipline);
                }
            }
        }

        private ElementId GetValidTypeId(AnalyticalMember member, Document doc)
        {
            ElementId typeId = member.GetTypeId();

            if (typeId == ElementId.InvalidElementId)
            {
                typeId = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_AnalyticalMember)
                    .WhereElementIsElementType()
                    .FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
            }

            return typeId;
        }
    }

    public class XYZComparer : IEqualityComparer<XYZ>
    {
        private readonly double _tolerance;
        public XYZComparer(double tolerance) => _tolerance = tolerance;

        public bool Equals(XYZ a, XYZ b) => a.DistanceTo(b) < _tolerance;

        public int GetHashCode(XYZ obj)
        {
            return (int)(Math.Round(obj.X / _tolerance) * 397) ^
                   (int)(Math.Round(obj.Y / _tolerance) * 397) ^
                   (int)(Math.Round(obj.Z / _tolerance) * 397);
        }
    }
}