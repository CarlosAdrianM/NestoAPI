using System.IO;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Recursos gráficos embebidos compartidos por los generadores de PDF (QuestPDF).
    /// Hoy expone el sello Madrid Excelente (versión reducida oficial, NestoAPI#244): recurso
    /// embebido en el ensamblado (robusto, sin depender de URLs de terceros que puedan caer ni de
    /// latencia de red al componer el PDF). Se carga una sola vez y se comparte entre todos los
    /// generadores. Si por lo que sea no se pudiera cargar, queda null y cada generador simplemente
    /// no lo pinta (nunca rompe la generación del PDF).
    /// </summary>
    public static class RecursosGraficos
    {
        /// <summary>Sello Madrid Excelente reducido (PNG, fondo transparente) o null si no se pudo cargar.</summary>
        public static readonly byte[] SelloMadridExcelente = CargarSelloMadridExcelente();

        private static byte[] CargarSelloMadridExcelente()
        {
            try
            {
                var assembly = typeof(RecursosGraficos).Assembly;
                using (var stream = assembly.GetManifestResourceStream("NestoAPI.Resources.SelloMadridExcelenteReducido.png"))
                {
                    if (stream == null)
                    {
                        return null;
                    }
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
