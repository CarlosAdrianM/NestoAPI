using NestoAPI.Models.Facturas;
using PdfiumViewer;
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// NestoAPI#806: manda un PDF a una impresora de Windows instalada en el servidor (RDS2016), por su nombre.
    /// </summary>
    public interface IImpresoraDocumentos
    {
        /// <summary>Imprime el PDF. Lanza con el motivo si no se ha podido (impresora que no existe, PDF vacío…).</summary>
        void Imprimir(byte[] pdf, string impresora, int copias, TipoBandejaImpresion bandeja);
    }

    /// <summary>
    /// NestoAPI#806: la impresión de verdad, con PdfiumViewer (lo mismo que Nesto en ServicioImpresionDocumentos.vb): rasteriza el
    /// PDF en un PrintDocument con la impresora, las copias y la bandeja. La bandeja se elige igual que en Nesto
    /// (<see cref="SelectorBandeja"/>).
    /// </summary>
    public class ImpresoraDocumentosPdfium : IImpresoraDocumentos
    {
        private static readonly object bloqueo = new object();
        private static bool resolverRegistrado;

        public void Imprimir(byte[] pdf, string impresora, int copias, TipoBandejaImpresion bandeja)
        {
            if (pdf == null || pdf.Length == 0)
            {
                throw new ArgumentException("El PDF está vacío.");
            }
            if (string.IsNullOrWhiteSpace(impresora))
            {
                throw new ArgumentException("No se ha indicado la impresora.");
            }
            RegistrarResolverPdfium();

            using (var stream = new MemoryStream(pdf))
            using (PdfDocument documento = PdfDocument.Load(stream))
            using (PrintDocument impresion = documento.CreatePrintDocument())
            {
                // En el servidor no hay nadie delante: nada de diálogos de «Imprimiendo…».
                impresion.PrintController = new StandardPrintController();
                impresion.PrinterSettings.PrinterName = impresora.Trim();
                if (!impresion.PrinterSettings.IsValid)
                {
                    throw new InvalidOperationException($"la impresora «{impresora.Trim()}» no está instalada en el servidor");
                }
                impresion.PrinterSettings.Copies = (short)Math.Max(1, Math.Min(copias, short.MaxValue));

                List<PaperSource> bandejas = impresion.PrinterSettings.PaperSources.Cast<PaperSource>().ToList();
                int indice = SelectorBandeja.Elegir(
                    bandejas.Select(b => new SelectorBandeja.Bandeja(b.SourceName, b.RawKind, (int)b.Kind)).ToList(), bandeja);
                if (indice >= 0)
                {
                    impresion.DefaultPageSettings.PaperSource = bandejas[indice];
                }

                impresion.Print();
            }
        }

        /// <summary>
        /// PdfiumViewer busca pdfium.dll en x64\ junto a su ensamblado o en la ruta de búsqueda del dominio; en IIS (con la copia
        /// en sombra de los ensamblados) puede no estar ahí: se le dice dónde lo deja el paquete (bin\x64) o la publicación (x64\).
        /// </summary>
        private static void RegistrarResolverPdfium()
        {
            lock (bloqueo)
            {
                if (resolverRegistrado)
                {
                    return;
                }
                PdfiumResolver.Resolve += (sender, e) =>
                {
                    string carpeta = Environment.Is64BitProcess ? "x64" : "x86";
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
                    var candidatas = new[]
                    {
                        AppDomain.CurrentDomain.RelativeSearchPath,
                        Path.Combine(baseDir, "bin"),
                        baseDir
                    };
                    foreach (string candidata in candidatas.Where(c => !string.IsNullOrWhiteSpace(c)))
                    {
                        foreach (string ruta in candidata.Split(';').Where(r => !string.IsNullOrWhiteSpace(r)))
                        {
                            string fichero = Path.Combine(ruta, carpeta, "pdfium.dll");
                            if (File.Exists(fichero))
                            {
                                e.PdfiumFileName = fichero;
                                return;
                            }
                        }
                    }
                };
                resolverRegistrado = true;
            }
        }
    }

    /// <summary>
    /// NestoAPI#806: la bandeja de la impresora para un <see cref="TipoBandejaImpresion"/>, como Nesto
    /// (ServicioImpresionDocumentos.vb): 1) por RawKind o Kind estándar; 2) si no, por nombre «Bandeja N» (HP y parecidas):
    /// Upper = la primera, Middle = la segunda (o la primera), Lower/Manual = la última, AutomaticFeed = la que diga «autom».
    /// </summary>
    public static class SelectorBandeja
    {
        public class Bandeja
        {
            public Bandeja(string nombre, int rawKind, int kind)
            {
                Nombre = nombre ?? string.Empty;
                RawKind = rawKind;
                Kind = kind;
            }

            public string Nombre { get; }
            public int RawKind { get; }
            public int Kind { get; }
        }

        /// <returns>El índice de la bandeja en la lista, o -1 si no hay ninguna que encaje (se usa la predeterminada).</returns>
        public static int Elegir(IList<Bandeja> bandejas, TipoBandejaImpresion tipo)
        {
            if (bandejas == null || bandejas.Count == 0)
            {
                return -1;
            }
            int valor = (int)tipo;
            for (int i = 0; i < bandejas.Count; i++)
            {
                if (bandejas[i].RawKind == valor || bandejas[i].Kind == valor)
                {
                    return i;
                }
            }

            List<int> numeradas = Enumerable.Range(0, bandejas.Count)
                .Where(i => bandejas[i].Nombre.Trim().ToLower().Contains("bandeja") && Regex.IsMatch(bandejas[i].Nombre, @"\d+"))
                .OrderBy(i => bandejas[i].Nombre, StringComparer.Ordinal)
                .ToList();
            if (tipo == TipoBandejaImpresion.AutomaticFeed)
            {
                return numeradas.Any()
                    ? Enumerable.Range(0, bandejas.Count).Where(i => bandejas[i].Nombre.ToLower().Contains("autom")).DefaultIfEmpty(-1).First()
                    : -1;
            }
            if (!numeradas.Any())
            {
                return -1;
            }
            switch (tipo)
            {
                case TipoBandejaImpresion.Upper:
                    return numeradas.First();
                case TipoBandejaImpresion.Middle:
                    return numeradas.Count >= 2 ? numeradas[1] : numeradas.First();
                default: // Lower y Manual (mismo valor)
                    return numeradas.Last();
            }
        }
    }
}
