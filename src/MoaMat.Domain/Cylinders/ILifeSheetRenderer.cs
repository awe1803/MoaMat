namespace MoaMat.Domain.Cylinders;

/// <summary>Turns a <see cref="CylinderLifeSheet"/> into a downloadable PDF document.</summary>
public interface ILifeSheetRenderer
{
    /// <summary>Renders the sheet.</summary>
    /// <param name="sheet">Content of the sheet.</param>
    /// <returns>The bytes of a PDF file.</returns>
    byte[] Render(CylinderLifeSheet sheet);
}
