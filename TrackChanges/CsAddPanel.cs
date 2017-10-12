#region Namespaces
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Architecture;
using BoundarySegment = Autodesk.Revit.DB.BoundarySegment;
using System.Reflection;
using System.Windows.Media.Imaging;
#endregion

namespace TrackChanges
{
    /// <remarks>
    /// This application's main class. The class must be Public.
    /// </remarks>
    public class CsAddPanel : IExternalApplication
    {
        // Both OnStartup and OnShutdown must be implemented as public method
        public Result OnStartup(UIControlledApplication application)
        {
            // Add a new ribbon panel
            RibbonPanel ribbonPanel = application.CreateRibbonPanel("NM MME");

            // Create a push button to trigger a command add it to the ribbon panel.
            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            PushButtonData buttonData = new PushButtonData("cmdTrackChanges",
               "Track Changes", thisAssemblyPath, "TrackChanges.Command");

            PushButton pushButton = ribbonPanel.AddItem(buttonData) as PushButton;

            // Optionally, other properties may be assigned to the button
            // a) tool-tip
            pushButton.ToolTip = "First toggle takes a current snapshot of this model, second toggle compares a fresh snapshot to previous and reports on differences.";

            // b) large bitmap
            Uri uriImage = new Uri(@"C:\ProgramData\Autodesk\Revit\Addins\TrackChanges\noun_1050.png");
            BitmapImage largeImage = new BitmapImage(uriImage);
            pushButton.LargeImage = largeImage;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            // nothing to clean up in this simple case
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class Command : IExternalCommand
    {
        #region Geometrical Comparison
        const double _eps = 1.0e-9;

        public static double Eps
        {
            get
            {
                return _eps;
            }
        }

        public static double MinLineLength
        {
            get
            {
                return _eps;
            }
        }

        public static double TolPointOnPlane
        {
            get
            {
                return _eps;
            }
        }

        public static bool IsZero(
          double a,
          double tolerance)
        {
            return tolerance > Math.Abs(a);
        }

        public static bool IsZero(double a)
        {
            return IsZero(a, _eps);
        }

        public static bool IsEqual(double a, double b)
        {
            return IsZero(b - a);
        }

        public static int Compare(double a, double b)
        {
            return IsEqual(a, b) ? 0 : (a < b ? -1 : 1);
        }

        public static int Compare(XYZ p, XYZ q)
        {
            int d = Compare(p.X, q.X);

            if (0 == d)
            {
                d = Compare(p.Y, q.Y);

                if (0 == d)
                {
                    d = Compare(p.Z, q.Z);
                }
            }
            return d;
        }
        #endregion // Geometrical Comparison

        #region String formatting
        /// <summary>
        /// Convert a string to a byte array.
        /// </summary>
        static byte[] GetBytes(string str)
        {
            byte[] bytes = new byte[str.Length
              * sizeof(char)];

            System.Buffer.BlockCopy(str.ToCharArray(),
              0, bytes, 0, bytes.Length);

            return bytes;
        }

        #region OBSOLETE
        /// <summary>
        /// Define a project identifier for the 
        /// given Revit document.
        /// </summary>
        public static string GetProjectIdentifier(
          Document doc)
        {
            SHA256 hasher = SHA256Managed.Create();

            string key = System.Environment.MachineName
              + ":" + doc.PathName;

            byte[] hashValue = hasher.ComputeHash(GetBytes(
              key));

            string hashb64 = Convert.ToBase64String(
              hashValue);

            return hashb64.Replace('/', '_');
        }
        #endregion // OBSOLETE

        /// <summary>
        /// Return an English plural suffix for the given 
        /// number of items, i.e. 's' for zero or more 
        /// than one, and nothing for exactly one.
        /// </summary>
        public static string PluralSuffix(int n)
        {
            return 1 == n ? "" : "s";
        }

        /// <summary>
        /// Return a string for a real number
        /// formatted to two decimal places.
        /// </summary>
        public static string RealString(double a)
        {
            return a.ToString("0.##");
        }

        /// <summary>
        /// Return a string for a UV point
        /// or vector with its coordinates 
        /// formatted to two decimal places.
        /// </summary>
        public static string PointString(UV p)
        {
            return string.Format("({0},{1})",
              RealString(p.U),
              RealString(p.V));
        }

        /// <summary>
        /// Return a string for an XYZ point
        /// or vector with its coordinates
        /// formatted to two decimal places.
        /// </summary>
        public static string PointString(XYZ p)
        {
            return string.Format("({0},{1},{2})",
              RealString(p.X),
              RealString(p.Y),
              RealString(p.Z));
        }

        /// <summary>
        /// Return a string for this bounding box
        /// with its coordinates formatted to two 
        /// decimal places.
        /// </summary>
        public static string BoundingBoxString(BoundingBoxUV bb)
        {
            return string.Format("({0},{1})",
              PointString(bb.Min),
              PointString(bb.Max));
        }

        /// <summary>
        /// Return a string for this bounding box
        /// with its coordinates formatted to two
        /// decimal places.
        /// </summary>
        public static string BoundingBoxString(
    BoundingBoxXYZ bb)
        {
            return string.Format("({0},{1})",
              PointString(bb.Min),
              PointString(bb.Max));
        }

        /// <summary>
        /// Return a string for this point array
        /// with its coordinates formatted to two
        /// decimal places.
        /// </summary>
        public static string PointArrayString(IList<XYZ> pts)
        {
            return string.Join(", ",
              pts.Select<XYZ, string>(
                p => PointString(p)));
        }

        /// <summary>
        /// Return a string for this curve with its
        /// tessellated point coordinates formatted
        /// to two decimal places.
        /// </summary>
        public static string CurveTessellateString(
          Curve curve)
        {
            return PointArrayString(curve.Tessellate());
        }

        /// <summary>
        /// Return a string for this curve with its
        /// tessellated point coordinates formatted
        /// to two decimal places.
        /// </summary>
        public static string LocationString(
          Location location)
        {
            LocationPoint lp = location as LocationPoint;
            LocationCurve lc = (null == lp)
              ? location as LocationCurve
              : null;

            return null == lp
              ? (null == lc
                ? null
                : CurveTessellateString(lc.Curve))
              : PointString(lp.Point);
        }

        /// <summary>
        /// Return a JSON string representing a dictionary
        /// of the given parameter names and values.
        /// </summary>
        public static string GetPropertiesJson(
          IList<Parameter> parameters)
        {
            int n = parameters.Count;
            List<string> a = new List<string>(n);
            foreach (Parameter p in parameters)
            {
                a.Add(string.Format("\"{0}\":\"{1}\"",
                  p.Definition.Name, p.AsValueString()));
            }
            a.Sort();
            string s = string.Join(",", a);
            return "{" + s + "}";
        }

        /// <summary>
        /// Return a string describing the given element:
        /// .NET type name,
        /// category name,
        /// family and symbol name for a family instance,
        /// element id and element name.
        /// </summary>
        public static string ElementDescription(
          Element e)
        {
            if (null == e)
            {
                return "<null>";
            }

            // For a wall, the element name equals the
            // wall type name, which is equivalent to the
            // family name ...

            FamilyInstance fi = e as FamilyInstance;

            string typeName = e.GetType().Name;

            string categoryName = (null == e.Category)
              ? string.Empty
              : e.Category.Name + " ";

            string familyName = (null == fi)
              ? string.Empty
              : fi.Symbol.Family.Name + " ";

            string symbolName = (null == fi
              || e.Name.Equals(fi.Symbol.Name))
                ? string.Empty
                : fi.Symbol.Name + " ";

            return string.Format("{0} {1}{2}{3}<{4} {5}>",
              typeName, categoryName, familyName,
              symbolName, e.Id.IntegerValue, e.Name);
        }

        public static string ElementDescription(
          Document doc,
          int element_id)
        {
            return ElementDescription(doc.GetElement(
              new ElementId(element_id)));
        }
        #endregion // String formatting

        #region Retrieve solid vertices
        /// <summary>
        /// Define equality between XYZ objects, ensuring 
        /// that almost equal points compare equal.
        /// </summary>
        class XyzEqualityComparer : IEqualityComparer<XYZ>
        {
            public bool Equals(XYZ p, XYZ q)
            {
                return p.IsAlmostEqualTo(q);
            }

            public int GetHashCode(XYZ p)
            {
                return PointString(p).GetHashCode();
            }
        }

        /// <summary>
        /// Add the vertices of the given solid to 
        /// the vertex lookup dictionary.
        /// </summary>
        static void AddVertices(
          Dictionary<XYZ, int> vertexLookup,
          Transform t,
          Solid s)
        {
            //Debug.Assert(0 < s.Edges.Size,
            //"expected a non-empty solid");

            foreach (Face f in s.Faces)
            {
                Mesh m = f.Triangulate();

                if (m != null)
                {
                    foreach (XYZ p in m.Vertices)
                    {
                        XYZ q = t.OfPoint(p);
                        if (!vertexLookup.ContainsKey(q))
                        {
                            vertexLookup.Add(q, 1);
                        }
                        else
                        {
                            ++vertexLookup[q];
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recursively add vertices of all solids found
        /// in the given geometry to the vertex lookup.
        /// Untested!
        /// </summary>
        static void AddVertices(
          Dictionary<XYZ, int> vertexLookup,
          Transform t,
          GeometryElement geo)
        {
            if (null == geo)
            {
                Debug.Assert(null != geo, "null GeometryElement");
                throw new System.ArgumentException("null GeometryElement");
            }

            foreach (GeometryObject obj in geo)
            {
                Solid solid = obj as Solid;

                if (null != solid)
                {
                    if (0 < solid.Faces.Size)
                    {
                        AddVertices(vertexLookup, t, solid);
                    }
                }
                else
                {
                    GeometryInstance inst = obj as GeometryInstance;

                    if (null != inst)
                    {
                        //GeometryElement geoi = inst.GetInstanceGeometry();
                        GeometryElement geos = inst.GetSymbolGeometry();

                        //Debug.Assert( null == geoi || null == geos,
                        //  "expected either symbol or instance geometry, not both" );

                        Debug.Assert(null != inst.Transform,
                          "null inst.Transform");

                        //Debug.Assert( null != inst.GetSymbolGeometry(),
                        //  "null inst.GetSymbolGeometry" );

                        if (null != geos)
                        {
                            AddVertices(vertexLookup,
                              inst.Transform.Multiply(t),
                              geos);
                        }
                    }
                }
            }
        }

        #region OBSOLETE
        /// <summary>
        /// Retrieve the first non-empty solid found for 
        /// the given element. In case the element is a 
        /// family instance, it may have its own non-empty
        /// solid, in which case we use that. Otherwise we 
        /// search the symbol geometry. If we use the 
        /// symbol geometry, we have to keep track of the 
        /// instance transform to map it to the actual
        /// instance project location.
        /// </summary>
        static Solid GetSolid2(Element e, Options opt)
        {
            GeometryElement geo = e.get_Geometry(opt);

            Dictionary<XYZ, int> a
              = new Dictionary<XYZ, int>(
                new XyzEqualityComparer());

            Solid solid = null;
            GeometryInstance inst = null;
            Transform t = Transform.Identity;

            // Some family elements have no own solids, so we
            // retrieve the geometry from the symbol instead; 
            // others do have own solids on the instance itself 
            // and no contents in the instance geometry 
            // (e.g. in rst_basic_sample_project.rvt).

            foreach (GeometryObject obj in geo)
            {
                solid = obj as Solid;

                if (null != solid
                  && 0 < solid.Faces.Size)
                {
                    break;
                }

                inst = obj as GeometryInstance;
            }

            if (null == solid && null != inst)
            {
                geo = inst.GetSymbolGeometry();
                t = inst.Transform;

                foreach (GeometryObject obj in geo)
                {
                    solid = obj as Solid;

                    if (null != solid
                      && 0 < solid.Faces.Size)
                    {
                        break;
                    }
                }
            }
            return solid;
        }
        #endregion // OBSOLETE

        /// <summary>
        /// Return a sorted list of all unique vertices 
        /// of all solids in the given element's geometry
        /// in lexicographical order.
        /// </summary>
        static List<XYZ> GetCanonicVertices(Element e)
        {
            GeometryElement geo = e.get_Geometry(new Options());
            Transform t = Transform.Identity;

            Dictionary<XYZ, int> vertexLookup
              = new Dictionary<XYZ, int>(
                new XyzEqualityComparer());

            AddVertices(vertexLookup, t, geo);

            List<XYZ> keys = new List<XYZ>(vertexLookup.Keys);

            keys.Sort(Compare);

            return keys;
        }
        #endregion // Retrieve solid vertices

        #region Retrieve elements of interest
        /// <summary>
        /// Retrieve all elements to track.
        /// It is up to you to decide which elements
        /// are of interest to you.
        /// </summary>
        /// 




        static IEnumerable<Element> GetTrackedElements(
    Document doc)
        {
            Categories cats = doc.Settings.Categories;

            List<ElementFilter> a = new List<ElementFilter>();

            foreach (Category c in cats)
            {
                if (CategoryType.Model == c.CategoryType)
                {
                    a.Add(new ElementCategoryFilter(c.Id));
                }
            }

            ElementFilter isModelCategory
              = new LogicalOrFilter(a);

            Options opt = new Options();

            return new FilteredElementCollector(doc)
              .WhereElementIsNotElementType()
              .WhereElementIsViewIndependent()
              .WherePasses(isModelCategory)
              .Where<Element>(e =>
               (null != e.get_BoundingBox(null))
               && (null != e.get_Geometry(opt)));
        }
        #endregion // Retrieve elements of interest

        #region Store element state
        /// <summary>
        /// Return a string representing the given element
        /// state. This is the information you wish to track.
        /// It is up to you to ensure that all data you are
        /// interested in really is included in this snapshot.
        /// In this case, we ignore all elements that do not
        /// have a valid bounding box.
        /// </summary>
        static string GetElementState(Element e)
        {
            string s = null;

            BoundingBoxXYZ bb = e.get_BoundingBox(null);

            if (null != bb)
            {
                List<string> properties = new List<string>();

                properties.Add(ElementDescription(e)
                  + " at " + LocationString(e.Location));

                if (!(e is FamilyInstance))
                {
                    properties.Add("Box="
                      + BoundingBoxString(bb));

                    properties.Add("Vertices="
                      + PointArrayString(GetCanonicVertices(e)));
                }

                properties.Add("Parameters="
                  + GetPropertiesJson(e.GetOrderedParameters()));

                s = string.Join(", ", properties);

                //Debug.Print( s );
            }
            return s;
        }
        #endregion // Store element state



        /// <summary>
        /// Return a string for a bounding box
        /// which may potentially be null
        /// with its coordinates formatted to two 
        /// decimal places.
        /// </summary>
        public static string BoundingBoxString2(BoundingBoxXYZ bb)
        {
            return null == bb
              ? "<null>"
              : BoundingBoxString(bb);
        }

        #region SnapRoomState
        /// <summary>
        /// defines helper method SnapRoomState to create snapshot of current room/space list
        /// </summary>

        /// <summary>
        /// 
        /// </summary>
        static Dictionary<int, string> SnapRoomState(IEnumerable<Element> r)
        {

            Dictionary<int, string> d = new Dictionary<int, string>();

            foreach (SpatialElement e in r)
            {
                Room room = e as Room;

                if (null != room)
                {
                    string s = GetRoomState(room);
                    d.Add(e.Id.IntegerValue, s);
                }
            }
            return d;
        }

        #endregion
        #region GetRoomState
        /// <summary>
        /// gets data on individual room spaces
        /// </summary>
        ///
        static string GetRoomState(Room room)
        {
            SpatialElementBoundaryOptions opt
                = new SpatialElementBoundaryOptions();

            string nr = room.Number;
            string name = room.Name;
            double area = room.Area;

            Location loc = room.Location;
            LocationPoint lp = loc as LocationPoint;
            XYZ p = (null == lp) ? XYZ.Zero : lp.Point;

            BoundingBoxXYZ bb = room.get_BoundingBox(null);

            IList<IList<BoundarySegment>> boundary
                = room.GetBoundarySegments(opt);

            int nLoops = boundary.Count;

            int nFirstLoopSegments = 0 < nLoops
                ? boundary[0].Count
                : 0;

            string rmData = string.Format(
                "Room nr. '{0}' named '{1}' at {2} with "
                + "bounding box {3} and area {4} sqf has "
                + "{5} loop{6} and {7} segment{8} in first "
                + "loop.",
                nr, name, PointString(p),
                BoundingBoxString2(bb), area, nLoops,
                PluralSuffix(nLoops), nFirstLoopSegments,
                PluralSuffix(nFirstLoopSegments));
            return rmData;
            //Debug.Print( rmData );
        }
        #endregion // Store element state

        #region GetRooms
        // Filtering for Room elements throws an exception:
        // Input type is of an element type that exists in 
        // the API, but not in Revit's native object model. 
        // Try using Autodesk.Revit.DB.SpatialElement 
        // instead, and then postprocessing the results to 
        // find the elements of interest.

        //FilteredElementCollector a 
        //  = new FilteredElementCollector( doc )
        //    .OfClass( typeof( Room ) );
        static IEnumerable<Element> GetRooms(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement));

        }

        #endregion


        #region Creating a Database State Snapshot
        /// <summary>
        /// Return a dictionary mapping element id values
        /// to hash codes of the element state strings. 
        /// This represents a snapshot of the current 
        /// database state.
        /// </summary>
        static Dictionary<int, string> GetSnapshot(
            IEnumerable<Element> a)
        {
            Dictionary<int, string> d
              = new Dictionary<int, string>();

            SHA256 hasher = SHA256Managed.Create();

            foreach (Element e in a)
            {
                //Debug.Print( e.Id.IntegerValue.ToString() 
                //  + " " + e.GetType().Name );

                string s = GetElementState(e);

                if (null != s)
                {
                    string hashb64 = Convert.ToBase64String(
                      hasher.ComputeHash(GetBytes(s)));

                    d.Add(e.Id.IntegerValue, hashb64);
                }
            }
            return d;
        }
        #endregion // Creating a Database State Snapshot

        #region Report differences
        /// <summary>
        /// Compare the start and end states and report the 
        /// differences found. In this implementation, we
        /// just store a hash code of the element state.
        /// If you choose to store the full string 
        /// representation, you can use that for comparison,
        /// and then report exactly what changed and the
        /// original values as well.
        /// </summary>
        static void ReportDifferences(
          Document doc,
          Dictionary<int, string> start_state,
          Dictionary<int, string> end_state,
          string room_report)
        {
            int n1 = start_state.Keys.Count;
            int n2 = end_state.Keys.Count;

            List<int> keys = new List<int>(start_state.Keys);

            foreach (int id in end_state.Keys)
            {
                if (!keys.Contains(id))
                {
                    keys.Add(id);
                }
            }

            keys.Sort();

            int n = keys.Count;

            Debug.Print(
              "{0} elements before, {1} elements after, {2} total",
              n1, n2, n);

            int nAdded = 0;
            int nDeleted = 0;
            int nModified = 0;
            int nIdentical = 0;
            List<string> report = new List<string>();

            foreach (int id in keys)
            {
                if (!start_state.ContainsKey(id))
                {
                    ++nAdded;
                    report.Add(id.ToString() + " added "
                      + ElementDescription(doc, id));
                }
                else if (!end_state.ContainsKey(id))
                {
                    ++nDeleted;
                    report.Add(id.ToString() + " deleted");
                }
                else if (start_state[id] != end_state[id])
                {
                    ++nModified;
                    report.Add(id.ToString() + " modified "
                      + ElementDescription(doc, id));
                }
                else
                {
                    ++nIdentical;
                }
            }

            string msg = string.Format(
              "Stopped tracking changes now.\r\n"
              + "{0} deleted, {1} added, {2} modified, "
              + "{3} identical elements:",
              nDeleted, nAdded, nModified, nIdentical);

            string s = string.Join("\r\n", report);

            string path = doc.PathName;

            Debug.Print(msg + "\r\n" + s);

            System.IO.File.WriteAllText("c:/Report.txt", msg + "\r\n" + s);
            TaskDialog dlg = new TaskDialog("Track Changes");
            dlg.MainInstruction = msg;
            dlg.MainContent = room_report;
            dlg.ExpandedContent = s;
            dlg.Show();
        }
        #endregion // Report differences

        #region Report differences for rooms
        /// <summary>
        /// Compare the start and end states and report the 
        /// differences found(rooms). For rooms, we store the 
        /// full string then compute the differences for reporting
        /// </summary>
        static string ReportDifferencesRooms(
          Document doc,
          Dictionary<int, string> start_state,
          Dictionary<int, string> end_state)
        {
            int n1 = start_state.Keys.Count;
            int n2 = end_state.Keys.Count;

            List<int> keys = new List<int>(start_state.Keys);

            foreach (int id in end_state.Keys)
            {
                if (!keys.Contains(id))
                {
                    keys.Add(id);
                }
            }

            keys.Sort();

            int n = keys.Count;

            Debug.Print(
              "{0} rooms before, {1} rooms after, {2} total",
              n1, n2, n);

            int nAdded = 0;
            int nDeleted = 0;
            int nModified = 0;
            int nIdentical = 0;
            List<string> report = new List<string>();

            foreach (int id in keys)
            {
                if (!start_state.ContainsKey(id))
                {
                    ++nAdded;
                    report.Add(id.ToString() + " added "
                      + ElementDescription(doc, id));
                }
                else if (!end_state.ContainsKey(id))
                {
                    ++nDeleted;
                    report.Add(id.ToString() + " deleted");
                }
                else if (start_state[id] != end_state[id])
                {
                    ++nModified;
                    report.Add(id.ToString() + " modified "
                      + ElementDescription(doc, id));
                }
                else
                {
                    ++nIdentical;
                }
            }

            string msg = string.Format(
              "Stopped tracking changes now.\r\n"
              + "{0} deleted, {1} added, {2} modified, "
              + "{3} identical rooms:",
              nDeleted, nAdded, nModified, nIdentical);

            string s = string.Join("\r\n", report);

            string path = doc.PathName;

            string fullString = msg + "\r\n" + s;

            Debug.Print(msg + "\r\n" + s);
            System.IO.File.WriteAllText("c:/ReportRooms.txt", msg + "\r\n" + s);
            return fullString;
        }
        #endregion // Report differences for rooms

        /// <summary>
        /// Current snapshot of database state.
        /// You could also store the entire element state 
        /// strings here, not just their hash code, to
        /// report their complete original and modified 
        /// values.
        /// </summary>
        static Dictionary<int, string> _start_state = null;
        static Dictionary<int, string> _start_rooms = null;

        #region External Command Mainline Execute Method
        public Result Execute(
    ExternalCommandData commandData,
    ref string message,
    ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;


            IEnumerable<Element> a = GetTrackedElements(doc);
            IEnumerable<Element> r = GetRooms(doc);

            if (null == _start_state)
            {
                _start_state = GetSnapshot(a);
                _start_rooms = SnapRoomState(r);
                TaskDialog.Show("Track Changes",
                  "Started tracking changes now.");
            }
            else
            {
                Dictionary<int, string> end_state = GetSnapshot(a);
                Dictionary<int, string> end_rooms = SnapRoomState(r);
                string room_report = ReportDifferencesRooms(doc, _start_rooms, end_rooms);
                ReportDifferences(doc, _start_state, end_state, room_report);
                _start_state = null;
                _start_rooms = null;
            }
            return Result.Succeeded;
        }
        #endregion // External Command Mainline Execute Method
    }
}

// Z:\a\rvt\little_house_2016.rvt
// C:\Program Files\Autodesk\Revit 2016\Samples\rac_advanced_sample_project.rvt
// Z:\a\rvt\rme_2016_empty.rvt