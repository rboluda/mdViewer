namespace MdViewer.Core.Documents;

/// <summary>Qué le ha pasado en disco al archivo de un documento abierto.</summary>
public enum DiskChange
{
    None,
    Modified,
    Deleted,
}
