using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using NestoAPI.Infraestructure.Agencias.PruebasEntrega;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Gls
{
    /// <summary>
    /// Prueba de entrega por agencia (10/11/26): la de GLS/ASM, de la MISMA consulta que el seguimiento (GetExpCli). En
    /// &lt;exp&gt;: pod (fecha y hora), NombreEntrega, DniEntrega y &lt;digitalizaciones&gt;&lt;digitalizacion&gt; con codtipo,
    /// tipo («POD (APP)»…), fecha, imagen (URL de pods.gls-spain.es/Extranet/public/AlbaranDesdeFirma.aspx) y observaciones.
    /// Comprobado el 10/11/26 con un envío entregado: la URL devuelve image/jpeg (el albarán firmado, ~190 KB). Se trae desde el
    /// servidor si es PDF o imagen; si no, se da el enlace para abrirlo en el navegador.
    /// </summary>
    public class PruebaEntregaGls : IProveedorPruebaEntrega
    {
        internal const string MENSAJE_SIN_CODIGO = "El envío no tiene albarán de GLS: no se puede pedir la prueba de entrega.";
        internal const string MENSAJE_NO_ENCONTRADO = "GLS no encuentra el envío: no se puede ver la prueba de entrega.";
        internal const string MENSAJE_NO_ENTREGADO = "GLS todavía no da el envío por entregado.";
        internal const string MENSAJE_SIN_IMAGEN = "GLS no da el albarán firmado de este envío.";

        private readonly IClienteTrackingGls cliente;
        private readonly IDescargadorDocumentosPruebaEntrega descargador;

        public PruebaEntregaGls(IClienteTrackingGls cliente, IDescargadorDocumentosPruebaEntrega descargador)
        {
            this.cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
            this.descargador = descargador ?? throw new ArgumentNullException(nameof(descargador));
        }

        public async Task<PruebaEntregaAgencia> ObtenerAsync(EnviosAgencia envio)
        {
            string albaran = envio.CodigoBarras?.Trim();
            if (string.IsNullOrEmpty(albaran))
            {
                return PruebaEntregaAgencia.NoDisponible(MENSAJE_SIN_CODIGO);
            }
            XDocument doc = await cliente.ConsultarAsync(albaran).ConfigureAwait(false);
            DatosEntregaGls datos = LeerEntrega(doc);
            if (datos == null)
            {
                return PruebaEntregaAgencia.NoDisponible(MENSAJE_NO_ENCONTRADO);
            }
            if (!datos.Entregado)
            {
                return PruebaEntregaAgencia.NoDisponible(MENSAJE_NO_ENTREGADO);
            }

            var prueba = new PruebaEntregaAgencia
            {
                Disponible = true,
                FechaHora = datos.FechaHora ?? envio.FechaEntrega,
                Receptor = datos.Receptor,
                DniReceptor = datos.Dni
            };
            string url = datos.UrlImagen;
            if (string.IsNullOrWhiteSpace(url))
            {
                prueba.Mensaje = MENSAJE_SIN_IMAGEN;
                return prueba;
            }
            DocumentoDescargado documento = await descargador.DescargarAsync(url).ConfigureAwait(false);
            if (documento == null)
            {
                // No es un PDF ni una imagen (o pesa demasiado): que lo abra en el navegador.
                prueba.UrlDocumento = url;
            }
            else
            {
                prueba.Documento = documento.Contenido;
                prueba.TipoDocumento = documento.Tipo;
            }
            return prueba;
        }

        internal class DatosEntregaGls
        {
            public bool Entregado { get; set; }
            public DateTime? FechaHora { get; set; }
            public string Receptor { get; set; }
            public string Dni { get; set; }
            public string UrlImagen { get; set; }
        }

        /// <summary>
        /// Lo de la entrega en la respuesta de GetExpCli; null si no hay &lt;exp&gt;. La imagen es la de la última digitalización
        /// de prueba de entrega (tipo con «POD»); si ninguna lo dice, la última que tenga imagen.
        /// </summary>
        internal static DatosEntregaGls LeerEntrega(XDocument doc)
        {
            XElement exp = doc?.Descendants().FirstOrDefault(e => e.Name.LocalName == "exp");
            if (exp == null)
            {
                return null;
            }
            string estado = (AgenciaRemotaGls.Valor(exp, "estado") ?? string.Empty).ToUpperInvariant();
            List<XElement> digitalizaciones = exp.Descendants()
                .Where(e => e.Name.LocalName == "digitalizacion" && !string.IsNullOrWhiteSpace(AgenciaRemotaGls.Valor(e, "imagen")))
                .ToList();
            XElement elegida = digitalizaciones
                .Where(d => (AgenciaRemotaGls.Valor(d, "tipo") ?? string.Empty).IndexOf("POD", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(d => AgenciaRemotaGls.ParsearFecha(AgenciaRemotaGls.Valor(d, "fecha")) ?? DateTime.MinValue)
                .LastOrDefault()
                ?? digitalizaciones.OrderBy(d => AgenciaRemotaGls.ParsearFecha(AgenciaRemotaGls.Valor(d, "fecha")) ?? DateTime.MinValue).LastOrDefault();

            DateTime? pod = AgenciaRemotaGls.ParsearFecha(AgenciaRemotaGls.Valor(exp, "pod"));
            bool devuelto = estado.Contains("DEVUEL") || estado.Contains("DEVOLUC");
            return new DatosEntregaGls
            {
                // Como el seguimiento (estado con «ENTREG»); con la fecha de entrega (pod) también vale.
                Entregado = !devuelto && (estado.Contains("ENTREG") || pod.HasValue),
                FechaHora = pod ?? AgenciaRemotaGls.ParsearFecha(AgenciaRemotaGls.Valor(exp, "FPEntrega")),
                Receptor = Limpio(AgenciaRemotaGls.Valor(exp, "NombreEntrega")),
                Dni = Limpio(AgenciaRemotaGls.Valor(exp, "DniEntrega")),
                UrlImagen = Limpio(elegida == null ? null : AgenciaRemotaGls.Valor(elegida, "imagen"))
            };
        }

        private static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }
}
