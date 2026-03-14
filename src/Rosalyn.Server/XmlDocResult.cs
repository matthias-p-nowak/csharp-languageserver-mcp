namespace Rosalyn.Server;

/// <summary>
/// Describes a single XML doc comment found by <c>get_xml_doc</c>.
/// </summary>
/// <param name="File">Repository-relative path to the source file.</param>
/// <param name="Line">1-based line number of the declaration.</param>
/// <param name="SymbolName">Name of the symbol the comment belongs to.</param>
/// <param name="Doc">The raw XML doc comment text.</param>
internal sealed record XmlDocResult(string File, int Line, string SymbolName, string Doc);
