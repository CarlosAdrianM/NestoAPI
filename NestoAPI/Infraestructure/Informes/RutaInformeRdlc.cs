using System;
using System.IO;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// 29/10/26 (Enrique, novedad 831): el ReportViewer resuelve una ReportPath relativa contra el directorio actual del
    /// proceso. Dentro de la petición web eso funcionaba, pero en código que sigue tras un ConfigureAwait(false) (el envío por
    /// correo de NestoAPI#724) el directorio es el de IIS: «No se puede encontrar una parte de la ruta de acceso
    /// 'c:\windows\system32\inetsrv\Models\Clientes\MandatoCoreSEPA.rdlc'». Se pasa siempre la ruta absoluta desde la raíz
    /// de la aplicación.
    /// </summary>
    public static class RutaInformeRdlc
    {
        public static string Absoluta(string rutaRelativa)
        {
            if (string.IsNullOrWhiteSpace(rutaRelativa) || Path.IsPathRooted(rutaRelativa))
            {
                return rutaRelativa;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rutaRelativa);
        }
    }
}
