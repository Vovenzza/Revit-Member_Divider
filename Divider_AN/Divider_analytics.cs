using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Divider_AN
{
    /*
      Вспомогательный класс для хранения данных об операции разделения.
      Содержит ID оригинального элемента, его исходную кривую и точки пересечения,
      рассчитанные для этой кривой.
     */
    internal class BeamSplitOperationData
    {
        public ElementId OriginalBeamId { get; }
        public Curve OriginalCurve { get; }
        public List<XYZ> IntersectionPoints { get; }

        public BeamSplitOperationData(ElementId beamId, Curve curve, List<XYZ> points)
        {
            OriginalBeamId = beamId;
            OriginalCurve = curve;
            IntersectionPoints = points;
        }
    }

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
                // Очистка текущего выбора пользователя
                uidoc.Selection.SetElementIds(new List<ElementId>());

                /*
                 * Этап 1: Выбор элементов пользователем.
                  Сначала выбираются аналитические стержни, которые нужно разделить.
                  Затем выбираются элементы (аналитические стержни и/или панели),
                  которые будут использоваться как "режущие".
                 */

                // 1.1 Выбор аналитических стержней для разделения
                IList<Reference> beamsToDivideRefs;
                try
                {
                    beamsToDivideRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new AnalyticalMemberFilter(),
                        "Select Analytical Members to divide" // Сообщение для пользователя на английском
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

                var beamsToDivide = beamsToDivideRefs
                    .Select(r => doc.GetElement(r))
                    .OfType<AnalyticalMember>()
                    .Where(b => b?.GetCurve() != null)
                    .ToList();

                if (!beamsToDivide.Any())
                {
                    message = "No valid analytical members selected to divide."; // Сообщение для пользователя на английском
                    TaskDialog.Show("Selection Error", message); // Заголовок и сообщение на английском
                    return Result.Failed;
                }

                // 1.2 Выбор "режущих" элементов (стержней и/или панелей)
                IList<Reference> cuttingElementRefs;
                try
                {
                    cuttingElementRefs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new AnalyticalMemberOrPanelFilter(),
                        "Select cutting elements (Analytical Members and/or Panels)" // Сообщение для пользователя на английском
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

                var cuttingElements = cuttingElementRefs
                    .Select(r => doc.GetElement(r))
                    .Where(e => e is AnalyticalMember || e is AnalyticalPanel)
                    .ToList();

                if (!cuttingElements.Any())
                {
                    message = "No valid cutting elements selected."; // Сообщение для пользователя на английском
                    TaskDialog.Show("Selection Error", message); // Заголовок и сообщение на английском
                    return Result.Failed;
                }

                /*
                  Этап 2: Сбор данных о пересечениях.
                  На этом этапе не происходит изменений в модели Revit.
                  Для каждого элемента из списка 'beamsToDivide' находятся точки пересечения
                  с элементами из списка 'cuttingElements'.
                  Информация о пересечениях сохраняется для последующей обработки.
                 */
                var splitOperations = new List<BeamSplitOperationData>();
                foreach (var beamToProcess in beamsToDivide)
                {
                    Curve originalCurve = beamToProcess.GetCurve();
                    if (originalCurve == null) continue;

                    // Исключаем сам 'beamToProcess' из списка режущих для него же
                    var relevantCuttingElementsForThisBeam = cuttingElements
                                                                .Where(ce => ce.Id != beamToProcess.Id)
                                                                .ToList();

                    if (!relevantCuttingElementsForThisBeam.Any()) continue;

                    List<XYZ> intersectionPoints = GetIntersectionPointsWithSelected(doc, beamToProcess, originalCurve, uidoc, relevantCuttingElementsForThisBeam);

                    if (intersectionPoints.Any())
                    {
                        splitOperations.Add(new BeamSplitOperationData(beamToProcess.Id, originalCurve, intersectionPoints));
                    }
                }

                if (!splitOperations.Any())
                {
                    TaskDialog.Show("Information", "No intersections found between selected elements. No members were split."); // Заголовок и сообщение на английском
                    return Result.Succeeded;
                }

                /*
                  Этап 3: Выполнение операций разделения.
                  На основе собранных данных о пересечениях, каждый элемент разделяется.
                  Каждая операция разделения выполняется в отдельной транзакции.
                 */
                int processedCount = 0;
                foreach (var operationData in splitOperations)
                {
                    AnalyticalMember currentBeamElementToSplit = doc.GetElement(operationData.OriginalBeamId) as AnalyticalMember;

                    if (currentBeamElementToSplit == null || currentBeamElementToSplit.GetCurve() == null)
                    {
                        Debug.WriteLine($"Element {operationData.OriginalBeamId} not found or invalid. Skipping split.");
                        continue;
                    }

                    using (var tx = new Transaction(doc, $"Split Member {operationData.OriginalBeamId}")) // Название транзакции на английском
                    {
                        tx.Start();
                        SplitBeam(doc, currentBeamElementToSplit, operationData.OriginalCurve, operationData.IntersectionPoints);
                        tx.Commit();
                        processedCount++;
                    }
                }

                TaskDialog.Show("Success", $"Processed and potentially split {processedCount} members based on {splitOperations.Count} planned operations."); // Заголовок и сообщение на английском
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message; // Сообщение об ошибке будет на английском, если оно из .NET/Revit API
                Debug.WriteLine($"Unhandled Exception: {ex.ToString()}");
                TaskDialog.Show("Error", $"An unexpected error occurred: {ex.Message}"); // Заголовок и сообщение на английском
                return Result.Failed;
            }
        }

        /*
          Находит точки пересечения для заданного 'beamToSplit' с элементами из 'specificCuttingElements'.
          'beamCurve' - это кривая элемента 'beamToSplit', для которой ищутся пересечения.
          Возвращает отсортированный список уникальных точек пересечения, лежащих на 'beamCurve'.
         */
        private List<XYZ> GetIntersectionPointsWithSelected(
            Document doc,
            AnalyticalMember beamToSplit,
            Curve beamCurve,
            UIDocument uidoc,
            List<Element> specificCuttingElements)
        {
            var tolerance = doc.Application.ShortCurveTolerance;
            var allHits = new List<XYZ>();

            View3D view3D = Get3DView(doc, uidoc);
            if (view3D == null)
            {
                Debug.WriteLine("Could not find a suitable 3D view for ReferenceIntersector.");
                return new List<XYZ>();
            }

            XYZ rayOrigin = beamCurve.GetEndPoint(0);
            XYZ rayDirection = (beamCurve.GetEndPoint(1) - rayOrigin).Normalize();

            // Разделяем режущие элементы на стержни и панели
            var cuttingMemberIds = specificCuttingElements
                                    .OfType<AnalyticalMember>()
                                    .Select(e => e.Id)
                                    .Where(id => id != beamToSplit.Id) // Элемент не режет сам себя
                                    .ToList();

            var cuttingPanelIds = specificCuttingElements
                                   .OfType<AnalyticalPanel>()
                                   .Select(e => e.Id)
                                   .ToList();

            // Поиск пересечений с режущими аналитическими стержнями
            if (cuttingMemberIds.Any())
            {
                var memberIntersector = new ReferenceIntersector(cuttingMemberIds, FindReferenceTarget.Element, view3D) { FindReferencesInRevitLinks = false };
                allHits.AddRange(memberIntersector.Find(rayOrigin, rayDirection)
                    .Where(rwc => rwc.Proximity > tolerance && rwc.Proximity < beamCurve.Length - tolerance)
                    .Select(rwc => rwc.GetReference()?.GlobalPoint)
                    .Where(pt => pt != null)
                    .Select(globalPt => beamCurve.Project(globalPt).XYZPoint));
            }

            // Поиск пересечений с режущими аналитическими панелями
            if (cuttingPanelIds.Any())
            {
                var panelIntersector = new ReferenceIntersector(cuttingPanelIds, FindReferenceTarget.Element, view3D) { FindReferencesInRevitLinks = false };
                allHits.AddRange(panelIntersector.Find(rayOrigin, rayDirection)
                    .Where(rwc => rwc.Proximity > tolerance && rwc.Proximity < beamCurve.Length - tolerance)
                    .Select(rwc => rwc.GetReference()?.GlobalPoint)
                    .Where(pt => pt != null)
                    .Select(globalPt => beamCurve.Project(globalPt).XYZPoint));
            }

            // Фильтрация и сортировка найденных точек
            return allHits
                .Where(p => p.DistanceTo(beamCurve.GetEndPoint(0)) > tolerance &&
                            p.DistanceTo(beamCurve.GetEndPoint(1)) > tolerance) // Исключаем точки на концах
                .Distinct(new XYZComparer(tolerance * 0.5)) // Убираем дубликаты с учетом допуска
                .OrderBy(p => beamCurve.Project(p).Parameter) // Сортируем по параметру на кривой
                .ToList();
        }

        /*
          Разделяет 'original' аналитический стержень на сегменты в точках 'intersections'.
          'curve' - это исходная кривая 'original' элемента, для которой были рассчитаны 'intersections'.
          Оригинальный элемент удаляется, создаются новые сегменты с скопированными параметрами.
         */
        private void SplitBeam(Document doc, AnalyticalMember original, Curve curve, List<XYZ> intersections)
        {
            var tolerance = doc.Application.ShortCurveTolerance;
            var allPoints = new List<XYZ> { curve.GetEndPoint(0) };

            // Добавляем точки пересечения, отсортированные по параметру на исходной кривой
            allPoints.AddRange(intersections
                .OrderBy(p => curve.Project(p).Parameter)
                .Distinct(new XYZComparer(tolerance * 0.5)));

            allPoints.Add(curve.GetEndPoint(1));

            // Финальная очистка и сортировка всех точек для создания сегментов
            allPoints = allPoints
                        .Distinct(new XYZComparer(tolerance * 0.1))
                        .OrderBy(p => curve.Project(p).Parameter)
                        .ToList();

            var createdSegments = new List<ElementId>();
            XYZ lastPoint = allPoints[0];

            for (int i = 1; i < allPoints.Count; i++)
            {
                XYZ currentPoint = allPoints[i];

                // Пропуск слишком коротких сегментов
                if (lastPoint.DistanceTo(currentPoint) < tolerance)
                {
                    lastPoint = currentPoint;
                    continue;
                }

                // Проверка, не существует ли уже такой сегмент (логика из оригинального кода)
                if (ShouldSkipSegment(doc, original, lastPoint, currentPoint, tolerance))
                {
                    lastPoint = currentPoint;
                    continue;
                }

                try
                {
                    Line newCurve = Line.CreateBound(lastPoint, currentPoint);
                    if (newCurve == null || newCurve.Length < tolerance) continue;

                    AnalyticalMember newSegment = AnalyticalMember.Create(doc, newCurve);
                    if (newSegment != null)
                    {
                        CopyParameters(doc, original, newSegment);
                        createdSegments.Add(newSegment.Id);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Segment creation failed between {lastPoint} and {currentPoint}: {ex.Message}");
                }
                lastPoint = currentPoint;
            }

            // Удаляем оригинальный элемент, если были созданы новые сегменты
            if (createdSegments.Any())
            {
                doc.Delete(original.Id);
            }
        }

        /*
          Проверяет, следует ли пропустить создание сегмента между 'start' и 'end'.
          Сегмент пропускается, если он слишком короткий или если идентичный сегмент уже существует.
         */
        private bool ShouldSkipSegment(Document doc, AnalyticalMember original, XYZ start, XYZ end, double tolerance)
        {
            if (start.DistanceTo(end) <= tolerance) return true;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(AnalyticalMember))
                .Cast<AnalyticalMember>()
                .Where(m => m.Id != original.Id) // Не сравниваем с самим собой (или с оригиналом, если он еще не удален)
                .Any(m =>
                {
                    Curve existingCurve = m.GetCurve();
                    return existingCurve != null && IsMatchingSegment(existingCurve, start, end, tolerance);
                });
        }


        // Проверяет, соответствует ли 'curve' сегменту, определенному точками 'start' и 'end'.

        private bool IsMatchingSegment(Curve curve, XYZ start, XYZ end, double tolerance)
        {
            if (curve == null) return false;

            XYZ curveStart = curve.GetEndPoint(0);
            XYZ curveEnd = curve.GetEndPoint(1);

            bool matchesForward = curveStart.IsAlmostEqualTo(start, tolerance) && curveEnd.IsAlmostEqualTo(end, tolerance);
            bool matchesBackward = curveStart.IsAlmostEqualTo(end, tolerance) && curveEnd.IsAlmostEqualTo(start, tolerance);

            return matchesForward || matchesBackward;
        }

        /*
          Получает подходящий 3D вид для использования с ReferenceIntersector.
          Предпочитает активный 3D вид, если он не является шаблоном.
         */
        private View3D Get3DView(Document doc, UIDocument uidoc)
        {
            if (uidoc.ActiveView is View3D active3DView && !active3DView.IsTemplate)
            {
                return active3DView;
            }
            return new FilteredElementCollector(doc)
                       .OfClass(typeof(View3D))
                       .Cast<View3D>()
                       .FirstOrDefault(v => !v.IsTemplate && v.CanBePrinted); // CanBePrinted как доп. проверка "полезности" вида
        }

        /*
          Копирует параметры из 'source' элемента в 'target' элемент.
          Пропускает некоторые системные или неизменяемые параметры.
         */
        private void CopyParameters(Document doc, AnalyticalMember source, AnalyticalMember target)
        {
            var skippedParamNames = new HashSet<string>
            {
                "Analytical Model Length", "Start Node", "End Node", "Type", // Системные и специфичные для геометрии
                BuiltInParameter.ID_PARAM.ToString(), // Уникальный ID элемента
            };

            foreach (Parameter srcParam in source.Parameters)
            {
                if (srcParam.IsReadOnly) continue;

                // Попытка получить параметр у цели по определению (надежнее для встроенных)
                Parameter tgtParam = target.get_Parameter(srcParam.Definition);
                if (tgtParam == null || tgtParam.IsReadOnly)
                {
                    // Если не найден по определению, пробуем по имени (для общих параметров)
                    tgtParam = target.LookupParameter(srcParam.Definition.Name);
                    if (tgtParam == null || tgtParam.IsReadOnly) continue;
                }

                if (skippedParamNames.Contains(srcParam.Definition.Name)) continue;
                if (srcParam.StorageType != tgtParam.StorageType) continue; // Типы хранения должны совпадать

                try
                {
                    switch (srcParam.StorageType)
                    {
                        case StorageType.Double: tgtParam.Set(srcParam.AsDouble()); break;
                        case StorageType.Integer: tgtParam.Set(srcParam.AsInteger()); break;
                        case StorageType.String: tgtParam.Set(srcParam.AsString()); break;
                        case StorageType.ElementId:
                            ElementId id = srcParam.AsElementId();
                            // Копируем ElementId, если это не ссылка на сам исходный элемент, его тип, и если элемент с таким ID существует
                            if (id != null && id != ElementId.InvalidElementId && id != source.Id && id != source.GetTypeId())
                            {
                                if (doc.GetElement(id) != null) tgtParam.Set(id);
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Parameter copy failed for '{srcParam.Definition.Name}': {ex.Message}");
                }
            }
        }

        #region Helper Classes (Фильтры выбора и компаратор для XYZ)

        public class AnalyticalMemberFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) =>
                elem != null && elem.Category != null &&
                elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_AnalyticalMember;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public class AnalyticalMemberOrPanelFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem == null || elem.Category == null) return false;
                int categoryId = elem.Category.Id.IntegerValue;
                return categoryId == (int)BuiltInCategory.OST_AnalyticalMember ||
                       categoryId == (int)BuiltInCategory.OST_AnalyticalPanel;
            }
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public class XYZComparer : IEqualityComparer<XYZ>
        {
            private readonly double _tolerance;
            public XYZComparer(double tolerance) => _tolerance = Math.Abs(tolerance);

            public bool Equals(XYZ a, XYZ b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a is null || b is null) return false;
                return a.IsAlmostEqualTo(b, _tolerance);
            }

            public int GetHashCode(XYZ obj)
            {
                if (obj is null) return 0;
                int hashX = Math.Round(obj.X / (_tolerance * 10)).GetHashCode();
                int hashY = Math.Round(obj.Y / (_tolerance * 10)).GetHashCode();
                int hashZ = Math.Round(obj.Z / (_tolerance * 10)).GetHashCode();
                return unchecked(hashX * 31 + hashY * 31 * 31 + hashZ * 31 * 31 * 31);
            }
        }
        #endregion
    }
}