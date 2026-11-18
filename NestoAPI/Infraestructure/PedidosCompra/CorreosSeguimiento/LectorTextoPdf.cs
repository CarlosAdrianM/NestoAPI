using System;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857e: cómo ha ido la lectura de los PDF adjuntos de un aviso de salida (se guarda en AdjuntoLeido).</summary>
    public enum EstadoPdfAdjunto
    {
        /// <summary>El remitente pide leer el PDF, pero el correo no trae ninguno.</summary>
        SinPdf,
        /// <summary>Se ha sacado texto de algún PDF.</summary>
        Leido,
        /// <summary>El PDF no tiene texto (escaneado, solo imagen).</summary>
        SinTexto,
        /// <summary>El PDF no se ha podido abrir (dañado, cifrado…).</summary>
        Ilegible,
        /// <summary>El PDF pasa del tope de tamaño (<see cref="LimitesPdfAdjuntos.MAXIMO_BYTES"/>): ni se descarga.</summary>
        Grande,
        /// <summary>Graph no ha dado los adjuntos (se trata como si no tuviera PDF).</summary>
        NoDescargado
    }

    /// <summary>NestoAPI#857e: lo sacado de UN PDF: el texto (solo en memoria) o por qué no.</summary>
    public class ResultadoTextoPdf
    {
        public EstadoPdfAdjunto Estado { get; set; }
        /// <summary>Solo con <see cref="EstadoPdfAdjunto.Leido"/>. Nunca se guarda.</summary>
        public string Texto { get; set; }
    }

    /// <summary>
    /// NestoAPI#857e: saca el texto de un PDF EN MEMORIA (nada a disco ni a la BD). Aísla la librería (PdfPig) para que el extractor y sus
    /// pruebas trabajen sobre el texto. Nunca lanza.
    /// </summary>
    public interface ILectorTextoPdf
    {
        ResultadoTextoPdf Leer(byte[] pdf, int maximoPaginas);
    }

    /// <summary>
    /// NestoAPI#857e: <see cref="ILectorTextoPdf"/> con PdfPig (UglyToad.PdfPig, Apache 2.0). El texto de cada página en el orden de
    /// lectura (con saltos de línea: las etiquetas y la agencia se buscan por líneas), como mucho <see cref="LimitesPdfAdjuntos.MAXIMO_PAGINAS"/>
    /// páginas y <see cref="LimitesPdfAdjuntos.LONGITUD_TEXTO"/> caracteres.
    /// </summary>
    public class LectorTextoPdfPig : ILectorTextoPdf
    {
        public ResultadoTextoPdf Leer(byte[] pdf, int maximoPaginas)
        {
            if (pdf == null || pdf.Length == 0)
            {
                return new ResultadoTextoPdf { Estado = EstadoPdfAdjunto.Ilegible };
            }
            try
            {
                var texto = new StringBuilder();
                using (PdfDocument documento = PdfDocument.Open(pdf))
                {
                    int paginas = Math.Min(documento.NumberOfPages, Math.Max(1, maximoPaginas));
                    for (int numero = 1; numero <= paginas && texto.Length < LimitesPdfAdjuntos.LONGITUD_TEXTO; numero++)
                    {
                        Page pagina = documento.GetPage(numero);
                        string deLaPagina = ContentOrderTextExtractor.GetText(pagina);
                        if (string.IsNullOrWhiteSpace(deLaPagina))
                        {
                            deLaPagina = pagina.Text;
                        }
                        _ = texto.AppendLine(deLaPagina);
                    }
                }
                return LimitesPdfAdjuntos.Resultado(texto.ToString());
            }
            catch (Exception)
            {
                return new ResultadoTextoPdf { Estado = EstadoPdfAdjunto.Ilegible };
            }
        }
    }

    /// <summary>NestoAPI#857e: los topes de la lectura de PDF adjuntos (constantes: si hace falta subirlos, aquí).</summary>
    public static class LimitesPdfAdjuntos
    {
        /// <summary>PDF por correo (los primeros, en el orden de Graph).</summary>
        public const int MAXIMO_PDFS = 3;
        /// <summary>5 MB: uno más grande ni se descarga.</summary>
        public const long MAXIMO_BYTES = 5L * 1024 * 1024;
        public const int MAXIMO_PAGINAS = 10;
        public const int LONGITUD_TEXTO = 100000;

        /// <summary>¿Parece un PDF por su tipo o su nombre?</summary>
        public static bool EsPdf(string nombre, string tipo) =>
            string.Equals(tipo?.Trim(), "application/pdf", StringComparison.OrdinalIgnoreCase)
            || (nombre ?? string.Empty).Trim().EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        /// <summary>Pura: el texto recortado, o «sin texto» si no hay ni una letra ni una cifra (PDF escaneado).</summary>
        public static ResultadoTextoPdf Resultado(string texto)
        {
            if (string.IsNullOrEmpty(texto) || !texto.Any(char.IsLetterOrDigit))
            {
                return new ResultadoTextoPdf { Estado = EstadoPdfAdjunto.SinTexto };
            }
            string limpio = texto.Replace("\r", string.Empty);
            return new ResultadoTextoPdf
            {
                Estado = EstadoPdfAdjunto.Leido,
                Texto = limpio.Length <= LONGITUD_TEXTO ? limpio : limpio.Substring(0, LONGITUD_TEXTO)
            };
        }

        /// <summary>Lee un flujo entero en memoria, sin pasar de <paramref name="maximo"/> bytes (null si pasa).</summary>
        internal static byte[] LeerConTope(Stream flujo, long maximo)
        {
            using (var memoria = new MemoryStream())
            {
                var bufer = new byte[81920];
                int leidos;
                while ((leidos = flujo.Read(bufer, 0, bufer.Length)) > 0)
                {
                    if (memoria.Length + leidos > maximo)
                    {
                        return null;
                    }
                    memoria.Write(bufer, 0, leidos);
                }
                return memoria.ToArray();
            }
        }
    }
}
