using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#519 (petición de Lidia, 23/09/26): pasar un pedido que todavía no ha salido a otro cliente
    /// (típicamente una ficha nueva recién creada) sin tener que montarlo otra vez.
    ///
    /// <para>Aquí viven, puras para poder probarlas sin BD, las decisiones: cuándo se puede, de dónde sale
    /// cada dato de la cabecera, qué líneas cambian de precio y cómo se cuenta lo que ha cambiado. La lectura
    /// y el guardado los hace <see cref="GestorCambioClientePedido"/>.</para>
    ///
    /// <para>La regla es «como si el pedido naciera para el cliente nuevo»: su contacto, sus condiciones de
    /// pago, su CCC, su IVA, su vendedor, su ruta, su periodo de facturación y sus precios. Lo que es del
    /// PEDIDO y no del cliente se queda: líneas, cantidades, almacén, fechas, comentarios, modo de servicio
    /// (depende del stock, no del cliente) y el modo de facturación «todo ahora» si se había elegido.</para>
    /// </summary>
    public static class CambioClientePedido
    {
        /// <summary>Cuántas líneas se nombran como mucho en un mensaje de rechazo.</summary>
        internal const int MAXIMO_LINEAS_EN_MENSAJE = 3;

        /// <summary>
        /// Por qué no se puede cambiar el cliente del pedido, o null si se puede. Primera versión (#519):
        /// cualquier cosa que ya haya salido o que esté atada al cliente anterior lo bloquea.
        /// </summary>
        /// <param name="numero">Número del pedido, para el mensaje.</param>
        /// <param name="notaEntrega">Si la cabecera es una nota de entrega.</param>
        /// <param name="lineas">TODAS las líneas del pedido.</param>
        /// <param name="prepagosVivos">Prepagos sin factura del pedido.</param>
        /// <param name="efectos">Efectos creados a mano en el pedido.</param>
        /// <param name="enviosAgencia">Números de los envíos de agencia del pedido (cualquier estado).</param>
        /// <param name="pagosTarjetaVivos">Cobros con tarjeta del pedido pendientes o autorizados.</param>
        internal static string MotivoNoSePuede(int numero, bool notaEntrega, IEnumerable<LinPedidoVta> lineas,
            int prepagosVivos, int efectos, IEnumerable<int> enviosAgencia, int pagosTarjetaVivos)
        {
            string prefijo = $"No se puede cambiar el cliente del pedido {numero}: ";
            if (notaEntrega)
            {
                return prefijo + "es una nota de entrega.";
            }

            List<LinPedidoVta> todas = lineas?.ToList() ?? new List<LinPedidoVta>();

            List<LinPedidoVta> facturadas = todas.Where(EstaFacturada).ToList();
            if (facturadas.Any())
            {
                return prefijo + $"{DescribirLineas(facturadas)} ya {(facturadas.Count == 1 ? "está facturada" : "están facturadas")}.";
            }
            List<LinPedidoVta> enAlbaran = todas.Where(l => l.Estado >= Constantes.EstadosLineaVenta.ALBARAN || l.Nº_Albarán != null).ToList();
            if (enAlbaran.Any())
            {
                return prefijo + $"{DescribirLineas(enAlbaran)} ya {(enAlbaran.Count == 1 ? "tiene" : "tienen")} albarán.";
            }
            List<LinPedidoVta> conPicking = todas.Where(l => (l.Picking ?? 0) != 0).ToList();
            if (conPicking.Any())
            {
                return prefijo + $"{DescribirLineas(conPicking)} ya {(conPicking.Count == 1 ? "tiene" : "tienen")} picking. " +
                    "Hay que quitar el picking antes de cambiar el cliente.";
            }

            List<int> envios = enviosAgencia?.ToList() ?? new List<int>();
            if (envios.Any())
            {
                return prefijo + $"tiene {(envios.Count == 1 ? "el envío de agencia" : "los envíos de agencia")} " +
                    $"{string.Join(", ", envios)} a nombre del cliente actual. Hay que borrarlo antes de cambiar el cliente.";
            }
            if (prepagosVivos > 0)
            {
                return prefijo + "tiene prepagos del cliente actual. Hay que quitarlos (o devolverlos) antes de cambiar el cliente.";
            }
            if (pagosTarjetaVivos > 0)
            {
                return prefijo + "tiene un cobro con tarjeta (o un enlace de pago) del cliente actual.";
            }
            if (efectos > 0)
            {
                return prefijo + "tiene efectos creados a mano con las condiciones del cliente actual. Hay que quitarlos antes de cambiar el cliente.";
            }
            return null;
        }

        private static bool EstaFacturada(LinPedidoVta linea)
        {
            return linea.Estado == Constantes.EstadosLineaVenta.FACTURA
                || !string.IsNullOrWhiteSpace(linea.Nº_Factura)
                || linea.YaFacturado;
        }

        private static string DescribirLineas(List<LinPedidoVta> lineas)
        {
            IEnumerable<string> nombradas = lineas.Take(MAXIMO_LINEAS_EN_MENSAJE)
                .Select(l => $"{l.Nº_Orden} ({l.Producto?.Trim()})");
            string texto = (lineas.Count == 1 ? "la línea " : "las líneas ") + string.Join(", ", nombradas);
            return lineas.Count > MAXIMO_LINEAS_EN_MENSAJE ? texto + $" y {lineas.Count - MAXIMO_LINEAS_EN_MENSAJE} más" : texto;
        }

        /// <summary>Por qué no vale la ficha de destino, o null si vale.</summary>
        internal static string MotivoFichaNoValida(Cliente ficha, string cliente, string contacto)
        {
            string quien = string.IsNullOrWhiteSpace(contacto) ? cliente?.Trim() : $"{cliente?.Trim()}/{contacto.Trim()}";
            if (ficha == null)
            {
                return $"No existe el cliente {quien}.";
            }
            if ((ficha.Estado ?? 0) < 0)
            {
                return $"La ficha del cliente {quien} está de baja.";
            }
            if (string.IsNullOrWhiteSpace(ficha.CIF_NIF))
            {
                // La misma regla que el PUT: no se modifican pedidos de clientes sin NIF.
                return $"La ficha del cliente {quien} no tiene NIF. Complétela antes de pasarle el pedido.";
            }
            return null;
        }

        /// <summary>
        /// Las condiciones de pago de la ficha para el importe del pedido: la de mayor ImporteMínimo que no lo
        /// supere. Es la misma regla que <c>PlantillaVentas/DireccionesEntrega</c>, con la que nace un pedido.
        /// </summary>
        internal static CondPagoCliente ResolverCondicionesPago(IEnumerable<CondPagoCliente> condiciones, decimal totalPedido)
        {
            return (condiciones ?? Enumerable.Empty<CondPagoCliente>())
                .OrderByDescending(c => c.ImporteMínimo)
                .FirstOrDefault(c => c.ImporteMínimo <= totalPedido);
        }

        /// <summary>
        /// Si el precio de la línea se vuelve a calcular con el cliente nuevo. Solo los productos «normales»:
        /// las líneas con oferta, con descuento de línea (regalos al 100 %, Ganavisiones, descuentos puestos a
        /// mano), a precio cero o en negativo (devoluciones) se quedan como están y las juzga la validación.
        /// </summary>
        internal static bool SeRecalculaElPrecio(LinPedidoVta linea)
        {
            return linea != null
                && linea.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                && !string.IsNullOrWhiteSpace(linea.Producto)
                && (linea.NºOferta ?? 0) == 0
                && linea.Descuento == 0
                && (linea.Cantidad ?? 0) > 0
                && (linea.Precio ?? 0) > 0;
        }

        /// <summary>
        /// El modo de facturación con el cliente nuevo. «Todo ahora» (3) es una decisión sobre el pedido y se
        /// respeta; «por entregas» o «al completar» es lo que dice la ficha (MantenerJunto), como al nacer.
        /// </summary>
        internal static byte ModoFacturacionParaCliente(byte? modoActual, bool mantenerJuntoActual, bool mantenerJuntoFicha)
        {
            byte efectivo = Constantes.Pedidos.ModosFacturacion.Efectivo(modoActual, mantenerJuntoActual);
            if (Constantes.Pedidos.ModosFacturacion.EsTodoAhora(efectivo))
            {
                return efectivo;
            }
            return mantenerJuntoFicha
                ? Constantes.Pedidos.ModosFacturacion.AL_COMPLETAR
                : Constantes.Pedidos.ModosFacturacion.POR_ENTREGAS;
        }

        /// <summary>
        /// Pasa la línea al cliente nuevo. Los importes NO se calculan aquí (dependen de los parámetros de IVA):
        /// los recalcula quien llama, con el IVA de la cabecera nueva.
        /// </summary>
        /// <param name="precioNuevo">El precio del cliente nuevo, o null si la línea conserva el suyo.</param>
        /// <returns>True si ha cambiado el precio, el descuento de producto o si aplica descuento.</returns>
        internal static bool AplicarALinea(LinPedidoVta linea, string cliente, string contacto,
            decimal descuentoCliente, decimal descuentoPP, ProductoPlantillaDTO precioNuevo)
        {
            linea.Nº_Cliente = cliente;
            linea.Contacto = contacto;
            linea.DescuentoCliente = descuentoCliente;
            linea.DescuentoPP = descuentoPP;
            if (precioNuevo == null)
            {
                return false;
            }
            bool cambia = linea.Precio != precioNuevo.precio
                || linea.DescuentoProducto != precioNuevo.descuento
                || linea.Aplicar_Dto != precioNuevo.aplicarDescuento;
            linea.Precio = precioNuevo.precio;
            linea.DescuentoProducto = precioNuevo.descuento;
            linea.Aplicar_Dto = precioNuevo.aplicarDescuento;
            return cambia;
        }

        /// <summary>
        /// La cabecera como la montaría un pedido nuevo del cliente: los mismos campos que Nesto copia de la
        /// dirección de entrega al elegir cliente (forma y plazos, IVA, vendedor, ruta, periodo) más el CCC,
        /// el contacto de cobro, NoComisiona y el modo de facturación.
        /// </summary>
        internal static void AplicarACabecera(CabPedidoVta cab, Cliente ficha, CondPagoCliente condiciones,
            bool cccObligatorio, string usuario, DateTime ahora)
        {
            byte modoFacturacion = ModoFacturacionParaCliente(cab.ModoFacturacion, cab.MantenerJunto, ficha.MantenerJunto);

            cab.Nº_Cliente = ficha.Nº_Cliente;
            cab.Contacto = ficha.Contacto;
            // Como al crear el pedido en Nesto: se cobra al contacto elegido, y los triggers miran sus
            // CondPagoClientes para decidir si los plazos son «de la ficha».
            cab.ContactoCobro = ficha.Contacto;
            cab.IVA = string.IsNullOrWhiteSpace(ficha.IVA) ? null : ficha.IVA;
            cab.Forma_Pago = condiciones.FormaPago;
            cab.PlazosPago = condiciones.PlazosPago;
            cab.CCC = cccObligatorio && !string.IsNullOrWhiteSpace(ficha.CCC) ? ficha.CCC : null;
            // El primer vencimiento y su visto bueno eran para los plazos del cliente anterior.
            cab.Primer_Vencimiento = null;
            cab.vtoBuenoPlazosPago = false;
            cab.Periodo_Facturacion = string.IsNullOrWhiteSpace(ficha.PeriodoFacturación)
                ? Constantes.Pedidos.PERIODO_FACTURACION_NORMAL
                : ficha.PeriodoFacturación;
            if (!string.IsNullOrWhiteSpace(ficha.Ruta))
            {
                cab.Ruta = ficha.Ruta;
            }
            cab.Vendedor = ficha.Vendedor;
            cab.NoComisiona = ficha.NoComisiona;
            cab.ModoFacturacion = modoFacturacion;
            cab.MantenerJunto = Constantes.Pedidos.ModosFacturacion.EsAlCompletar(modoFacturacion);
            cab.Usuario = usuario;
            cab.Fecha_Modificación = ahora;
        }

        /// <summary>
        /// Lo que ha cambiado entre el pedido de antes y el de después, en lenguaje de usuario. Se calcula con
        /// el pedido RELEÍDO al final, así cuenta también lo que han hecho los triggers y el PUT (portes).
        /// </summary>
        internal static List<string> DescribirCambios(PedidoVentaDTO antes, PedidoVentaDTO despues)
        {
            List<string> cambios = new List<string>();
            if (antes == null || despues == null)
            {
                return cambios;
            }
            Anotar(cambios, "Cliente", $"{antes.cliente?.Trim()}/{antes.contacto?.Trim()}", $"{despues.cliente?.Trim()}/{despues.contacto?.Trim()}");
            Anotar(cambios, "IVA", Texto(antes.iva, "sin IVA"), Texto(despues.iva, "sin IVA"));
            Anotar(cambios, "Forma de pago", Texto(antes.formaPago), Texto(despues.formaPago));
            Anotar(cambios, "Plazos de pago", Texto(antes.plazosPago), Texto(despues.plazosPago));
            Anotar(cambios, "CCC", Texto(antes.ccc, "sin CCC"), Texto(despues.ccc, "sin CCC"));
            Anotar(cambios, "Vendedor", Texto(antes.vendedor), Texto(despues.vendedor));
            Anotar(cambios, "Ruta", Texto(antes.ruta), Texto(despues.ruta));
            Anotar(cambios, "Periodo de facturación", Texto(antes.periodoFacturacion), Texto(despues.periodoFacturacion));
            Anotar(cambios, "Facturación",
                Constantes.Pedidos.ModosFacturacion.Nombre(Constantes.Pedidos.ModosFacturacion.Efectivo(antes.modoFacturacion, antes.mantenerJunto)),
                Constantes.Pedidos.ModosFacturacion.Nombre(Constantes.Pedidos.ModosFacturacion.Efectivo(despues.modoFacturacion, despues.mantenerJunto)));

            List<LineaPedidoVentaDTO> lineasAntes = antes.Lineas?.ToList() ?? new List<LineaPedidoVentaDTO>();
            List<LineaPedidoVentaDTO> lineasDespues = despues.Lineas?.ToList() ?? new List<LineaPedidoVentaDTO>();
            foreach (LineaPedidoVentaDTO lineaDespues in lineasDespues)
            {
                LineaPedidoVentaDTO lineaAntes = lineasAntes.FirstOrDefault(l => l.id == lineaDespues.id);
                if (lineaAntes == null)
                {
                    cambios.Add($"Línea nueva: {lineaDespues.Producto?.Trim()} {lineaDespues.texto?.Trim()} ({Importe(lineaDespues.BaseImponible)})");
                    continue;
                }
                if (lineaAntes.PrecioUnitario != lineaDespues.PrecioUnitario || lineaAntes.DescuentoProducto != lineaDespues.DescuentoProducto)
                {
                    cambios.Add($"Precio de {lineaDespues.Producto?.Trim()}: {PrecioYDescuento(lineaAntes)} → {PrecioYDescuento(lineaDespues)}");
                }
            }
            foreach (LineaPedidoVentaDTO lineaQuitada in lineasAntes.Where(a => lineasDespues.All(d => d.id != a.id)))
            {
                cambios.Add($"Línea quitada: {lineaQuitada.Producto?.Trim()} {lineaQuitada.texto?.Trim()} ({Importe(lineaQuitada.BaseImponible)})");
            }
            Anotar(cambios, "Total del pedido", Importe(antes.Total), Importe(despues.Total));
            return cambios;
        }

        private static void Anotar(List<string> cambios, string que, string antes, string despues)
        {
            if (!string.Equals(antes, despues, StringComparison.Ordinal))
            {
                cambios.Add($"{que}: {antes} → {despues}");
            }
        }

        private static string Texto(string valor, string siVacio = "(vacío)")
        {
            return string.IsNullOrWhiteSpace(valor) ? siVacio : valor.Trim();
        }

        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        private static string Importe(decimal importe) => importe.ToString("N2", ES) + " €";

        private static string PrecioYDescuento(LineaPedidoVentaDTO linea)
        {
            string precio = Importe(linea.PrecioUnitario);
            return linea.DescuentoProducto == 0 ? precio : $"{precio} -{(linea.DescuentoProducto * 100).ToString("0.##", ES)} %";
        }
    }
}
