using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Globalization;
using System.Net;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// LA REGLA (pura, Carlos 25/11/26) de qué documento NO sale hacia fuera (PDF, JSON, enlace de descarga, correo, WhatsApp):
    /// (a) cualquier documento de la empresa espejo (<see cref="Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO"/>);
    /// (b) una factura de una serie sin descarga (<see cref="ISerieFactura.CorreoDesdeFactura"/> null: GB), lo de siempre;
    /// (c) el pedido, albarán, proforma o presupuesto de un pedido que va a la empresa espejo
    /// (<see cref="ServicioTraspasoEmpresa.VaAEmpresaEspejo"/>: IVA de la cabecera NULL o en blanco).
    /// Mensaje único de usted, sin explicar el porqué: <see cref="Mensaje"/> (403).
    /// SOLO para las salidas: lo interno (Facturar rutas, nota de entrega del repartidor, packing, etiquetas, correo interno
    /// del pedido…) lee y genera el documento sin pasar por aquí. Por eso no está dentro de GestorFacturas.LeerPedido/LeerAlbaran.
    /// </summary>
    public static class ReglaDocumentoNoDescargable
    {
        public static string Mensaje(string numero) => $"El documento {numero?.Trim()} no se puede descargar.";

        /// <summary>La excepción de las salidas: 403 con <see cref="Mensaje"/>.</summary>
        public static NestoBusinessException Excepcion(string numero)
            => new NestoBusinessException(Mensaje(numero)) { StatusCode = HttpStatusCode.Forbidden };

        /// <summary>(a) ¿Es de la empresa espejo?</summary>
        public static bool EsEmpresaEspejo(string empresa)
            => string.Equals(empresa?.Trim(), Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO, StringComparison.Ordinal);

        /// <summary>(b) ¿Es una factura de una serie sin descarga? La serie son los dos primeros caracteres del número.</summary>
        public static bool EsSerieSinDescarga(string numeroFactura)
        {
            string numero = numeroFactura?.Trim();
            if (numero == null || numero.Length < 2)
            {
                return false;
            }
            try
            {
                ISerieFactura serie = GestorFacturas.LeerSerie(numero.Substring(0, 2));
                return serie != null && serie.CorreoDesdeFactura == null;
            }
            catch (Exception)
            {
                // Serie que no se puede leer: dará «no existe» después
                return false;
            }
        }

        /// <summary>¿No se sirve esta factura? (a) o (b).</summary>
        public static bool NoSeSirveFactura(string empresa, string numeroFactura)
            => EsEmpresaEspejo(empresa) || EsSerieSinDescarga(numeroFactura);

        /// <summary>
        /// ¿No se sirve el documento (pedido, albarán, proforma o presupuesto) de este pedido? (a) o (c).
        /// <paramref name="ivaCabecera"/> es <c>CabPedidoVta.IVA</c> tal como está guardado.
        /// </summary>
        public static bool NoSeSirvePedido(string empresa, string ivaCabecera)
            => EsEmpresaEspejo(empresa) || ServicioTraspasoEmpresa.VaAEmpresaEspejo(ivaCabecera);

        /// <summary>
        /// GET api/PedidosVenta/{empresa}/{pedido}/DocumentosImpresion (Cajas de Nesto, tras cobrar; Carlos 25/11/26: «No se puede
        /// imprimir en ningún caso»). Elige el documento como GestorFacturacionRutas.ObtenerDocumentosImpresion (factura si viene y no
        /// es FDM; si no, albarán; si no, nota de entrega del pedido) y devuelve su número si NO se imprime: <paramref name="pedidoNoSale"/>
        /// (empresa espejo o pedido sin IVA, <see cref="NoSeSirvePedido"/>) o una factura que no se sirve (<see cref="NoSeSirveFactura"/>).
        /// Null = se imprime como siempre.
        /// </summary>
        public static string DocumentoQueNoSeImprime(string empresa, bool pedidoNoSale, int pedido, string numeroFactura, int? numeroAlbaran)
        {
            bool esFactura = !string.IsNullOrWhiteSpace(numeroFactura)
                && numeroFactura != Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES;
            string numero = esFactura
                ? numeroFactura.Trim()
                : numeroAlbaran.HasValue && numeroAlbaran.Value > 0
                    ? numeroAlbaran.Value.ToString(CultureInfo.InvariantCulture)
                    : pedido.ToString(CultureInfo.InvariantCulture);
            if (pedidoNoSale || EsEmpresaEspejo(empresa))
            {
                return numero;
            }
            return esFactura && NoSeSirveFactura(empresa, numeroFactura) ? numero : null;
        }

        /// <summary>Como GestorFacturas.LeerFacturas: un número entero es un pedido; si no, una factura.</summary>
        public static bool EsNumeroDePedido(string numero, out int pedido)
            => int.TryParse(numero?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out pedido);
    }

    /// <summary>
    /// Lo que la regla necesita leer (la cabecera del pedido), para las SALIDAS. Lanza la excepción de la regla (403) si el
    /// documento no se sirve. Un pedido o albarán que no existe no se rechaza aquí: sigue su camino y da su «no existe».
    /// </summary>
    public class ComprobadorDocumentoDescargable
    {
        private readonly IServicioFacturas servicio;

        public ComprobadorDocumentoDescargable(IServicioFacturas servicio)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
        }

        /// <summary>El número de GET api/Facturas (y del JSON): entero = pedido; si no, factura.</summary>
        public void ComprobarDocumento(string empresa, string numero)
        {
            if (ReglaDocumentoNoDescargable.EsNumeroDePedido(numero, out int pedido))
            {
                ComprobarPedido(empresa, pedido);
                return;
            }
            ComprobarFactura(empresa, numero);
        }

        public void ComprobarFactura(string empresa, string numeroFactura)
        {
            if (ReglaDocumentoNoDescargable.NoSeSirveFactura(empresa, numeroFactura))
            {
                throw ReglaDocumentoNoDescargable.Excepcion(numeroFactura);
            }
        }

        public void ComprobarPedido(string empresa, int pedido)
        {
            if (ReglaDocumentoNoDescargable.EsEmpresaEspejo(empresa))
            {
                throw ReglaDocumentoNoDescargable.Excepcion(pedido.ToString(CultureInfo.InvariantCulture));
            }
            CabPedidoVta cab = servicio.CargarCabPedido(empresa?.Trim(), pedido);
            if (cab != null && ReglaDocumentoNoDescargable.NoSeSirvePedido(empresa, cab.IVA))
            {
                throw ReglaDocumentoNoDescargable.Excepcion(pedido.ToString(CultureInfo.InvariantCulture));
            }
        }

        public void ComprobarAlbaran(string empresa, int albaran)
        {
            if (ReglaDocumentoNoDescargable.EsEmpresaEspejo(empresa))
            {
                throw ReglaDocumentoNoDescargable.Excepcion(albaran.ToString(CultureInfo.InvariantCulture));
            }
            CabPedidoVta cab = servicio.CargarCabPedidoPorAlbaran(empresa?.Trim(), albaran);
            if (cab != null && ReglaDocumentoNoDescargable.NoSeSirvePedido(empresa, cab.IVA))
            {
                throw ReglaDocumentoNoDescargable.Excepcion(albaran.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
