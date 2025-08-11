# Revit Analytical Member Divider

A high-precision Autodesk Revit 2023 add-in for splitting analytical members (like beams, columns, and braces) at their exact points of intersection with other analytical elements. This tool is essential for structural engineers and modelers who need to break down continuous analytical lines into discrete, connected segments for accurate analysis and documentation.



---

### What It Does

The script provides a streamlined workflow for accurately dividing analytical members:

1.  **Select Members to Split:** You first select the analytical members (beams, columns, etc.) that you want to divide.
2.  **Select Cutting Elements:** Next, you select the elements that will act as the "cutters." These can be other analytical members (lines) or even analytical panels (surfaces).
3.  **Automatic Splitting & Data Transfer:** The script uses Revit's powerful 3D intersection engine to find the precise points where the elements cross. It then automatically splits the original members at these points, deletes the original, and transfers all relevant properties to the new segments.

### Key Features

*   **Accurate 3D Intersection:** Uses Revit's `ReferenceIntersector` to find true 3D intersection points, ensuring high precision even in complex frames.
*   **Flexible Cutting Tools:** Split members using either other members (line-line intersection) or panels (line-plane intersection).
*   **Full Parameter Preservation:** Automatically copies all relevant data (material, section properties, custom parameters) from the original member to the newly created segments.
*   **Batch Processing:** Select and process multiple members and cutting elements in a single operation.
*   **Intelligent & Safe:** Each member is split within its own transaction. The script intelligently avoids creating zero-length segments and handles cases with no intersections gracefully.
*   **Clean Workflow:** A simple, guided two-step selection process keeps the user in full control.

### How to Use

1.  **Load the Add-in:** Use the installer or manually load the `.dll` and `.addin` files into Revit.
2.  **Run the Command:** Find the "Divide Analytical Members" command in your Revit Add-ins tab.
3.  **Select Members:** The Revit status bar will prompt you to `Select Analytical Members to divide`. Select the beams, columns, or braces you want to split and click "Finish" on the top bar.
4.  **Select Cutters:** You will then be prompted to `Select cutting elements`. Select the members or panels that will define the split points and click "Finish".
5.  **Done!** The script will find all intersections and split the members accordingly, showing a success message when complete.

### AI-Assisted Development ("Vibecoding")

This script was developed with significant assistance from AI. This "Vibecoding" process involved translating a clear functional vision and complex geometric logic into robust C# code through collaboration with an AI partner. The AI helped structure the code, implement advanced Revit API features, and refine the workflow, acting as a powerful tool to bring the initial idea to life.

### Installation

There are two ways to install this add-in:

#### Option 1: Easy Installer (Recommended)

1.  Go to the [**Releases Page**](https://github.com/Vovenzza/Revit-Member_Divider/releases) for this repository.
2.  Download the `.exe` installer from the latest release.
3.  Run the installer. It will automatically place the necessary files in your Revit 2023 add-ins folder.

#### Option 2: Manual Installation

1.  **Compile the Code:** Open the project in Visual Studio and build the solution to create a `Divider_AN.dll` file (or similar).
2.  **Create a Manifest File:** In Revit's add-in directory (`%appdata%\Autodesk\Revit\Addins\2023`), create a file named `AnalyticalMemberDivider.addin`. Paste the following into it, ensuring the `<Assembly>` path and `<AddInId>` are correct.

    ```xml
    <?xml version="1.0" encoding="utf-8"?>
    <RevitAddIns>
      <AddIn Type="Command">
        <Name>Divide Analytical Members</Name>
        <FullClassName>Divider_AN.Divider_analytics</FullClassName>
        <Text>Divide Analytical Members</Text>
        <Description>Splits analytical members at their intersection with other members or panels.</Description>
        <VisibilityMode>AlwaysVisible</VisibilityMode>
        <Assembly>"C:\Path\To\Your\Project\bin\Debug\Divider_AN.dll"</Assembly>
        <AddInId>{GENERATE-A-NEW-GUID-HERE}</AddInId>
        <VendorId>YourName</VendorId>
        <VendorDescription>Your Company or Website</VendorDescription>
      </AddIn>
    </RevitAddIns>
    ```
    *   **Crucial:** Replace `"C:\Path\To\Your\..."` with the actual path to your new DLL.
    *   **Crucial:** Generate a **new GUID** (in Visual Studio via `Tools > Create GUID`) and paste it into the `<AddInId>` field to avoid conflicts.

---

**Disclaimer:** This is a utility script. Always save and back up your Revit models before running commands that modify geometry. Use at your own risk.
