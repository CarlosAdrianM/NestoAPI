using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#876 (Carlos 26/11/26): pasar a otro cliente un pedido que YA tiene picking (sin albarán, factura ni envío de
    /// agencia). Se puede si y solo si, después de recalcularlo para el cliente nuevo («como si naciera para él», la regla de
    /// siempre de <see cref="CambioClientePedido"/>), ninguna línea cambia de precio, descuentos, IVA ni importes y la cabecera
    /// no cambia de IVA: es lo que pasa cuando la ficha nueva es copia de la anterior (cambio de forma fiscal). Si algo
    /// cambiaría, se rechaza diciendo qué, sin tocar nada. El picking y las reservas no se tocan.
    ///
    /// <para>Aquí, puras: la foto de lo que se compara (y con la que se deja la línea como estaba si no se hace), la comparación
    /// y los textos.</para>
    /// </summary>
    public static class ReglaCambioClienteConPicking
    {
        /// <summary>Código del 409 cuando el pedido tiene picking y algo cambiaría al recalcularlo.</summary>
        public const string CODIGO_CAMBIARIA = "CAMBIO_CLIENTE_CON_PICKING_CAMBIA";

        /// <summary>Tipo de la campana a Almacén.</summary>
        public const string TIPO_AVISO = "PedidoCambiaDeCliente";

        /// <summary>Cuántas líneas se nombran como mucho en el mensaje.</summary>
        internal const int MAXIMO_LINEAS = 5;

        /// <summary>
        /// Importes: se comparan al céntimo (la línea guardada viene de un money de 4 decimales y el recálculo da precisión
        /// completa: sin redondear, el IVA de 21 % de 12,35 € «cambiaría» sin que cambie nada). Precio y descuentos, a 4
        /// decimales (lo que guarda la BD).
        /// </summary>
        internal static bool IgualesImporte(decimal a, decimal b) => Math.Round(a, 2, MidpointRounding.AwayFromZero) == Math.Round(b, 2, MidpointRounding.AwayFromZero);

        internal static bool IgualesPrecio(decimal? a, decimal? b) => Math.Round(a ?? 0, 4, MidpointRounding.AwayFromZero) == Math.Round(b ?? 0, 4, MidpointRounding.AwayFromZero);

        public static List<FotoLineaCambioCliente> Fotografiar(IEnumerable<LinPedidoVta> lineas)
        {
            return (lineas ?? Enumerable.Empty<LinPedidoVta>()).Select(FotoLineaCambioCliente.De).ToList();
        }

        /// <summary>Deja las líneas como en la foto (no se ha hecho el cambio, o era un ensayo).</summary>
        public static void Restaurar(IEnumerable<LinPedidoVta> lineas, IEnumerable<FotoLineaCambioCliente> fotos)
        {
            Dictionary<int, FotoLineaCambioCliente> porOrden = (fotos ?? Enumerable.Empty<FotoLineaCambioCliente>())
                .GroupBy(f => f.NumeroOrden).ToDictionary(g => g.Key, g => g.First());
            foreach (LinPedidoVta linea in lineas ?? Enumerable.Empty<LinPedidoVta>())
            {
                if (porOrden.TryGetValue(linea.Nº_Orden, out FotoLineaCambioCliente foto))
                {
                    foto.Restaurar(linea);
                }
            }
        }

        /// <summary>
        /// Lo que cambiaría en las líneas (precio, descuentos, IVA, importes) y en el IVA de la cabecera. Vacía = nada: el
        /// pedido se puede pasar aunque tenga picking. El cliente y el contacto de la línea no cuentan (es lo que se cambia).
        /// </summary>
        public static List<string> Diferencias(IEnumerable<FotoLineaCambioCliente> antes, IEnumerable<FotoLineaCambioCliente> despues,
            string ivaCabeceraAntes, string ivaCabeceraDespues)
        {
            List<string> diferencias = new List<string>();
            if (!string.Equals(Limpio(ivaCabeceraAntes), Limpio(ivaCabeceraDespues), StringComparison.OrdinalIgnoreCase))
            {
                diferencias.Add($"IVA del pedido: {Texto(ivaCabeceraAntes, "sin IVA")} → {Texto(ivaCabeceraDespues, "sin IVA")}");
            }
            Dictionary<int, FotoLineaCambioCliente> previas = (antes ?? Enumerable.Empty<FotoLineaCambioCliente>())
                .GroupBy(f => f.NumeroOrden).ToDictionary(g => g.Key, g => g.First());
            foreach (FotoLineaCambioCliente nueva in despues ?? Enumerable.Empty<FotoLineaCambioCliente>())
            {
                if (!previas.TryGetValue(nueva.NumeroOrden, out FotoLineaCambioCliente vieja))
                {
                    continue;
                }
                List<string> deLinea = DiferenciasLinea(vieja, nueva);
                if (deLinea.Any())
                {
                    diferencias.Add($"Línea {nueva.NumeroOrden} ({Texto(nueva.Producto, "sin producto")}): {string.Join(", ", deLinea)}");
                }
            }
            return diferencias;
        }

        internal static List<string> DiferenciasLinea(FotoLineaCambioCliente a, FotoLineaCambioCliente b)
        {
            List<string> d = new List<string>();
            if (!IgualesPrecio(a.Precio, b.Precio))
            {
                d.Add($"precio {Importe(a.Precio ?? 0)} → {Importe(b.Precio ?? 0)}");
            }
            if (!IgualesPrecio(a.DescuentoProducto, b.DescuentoProducto) || a.AplicarDto != b.AplicarDto)
            {
                d.Add($"descuento de producto {Porcentaje(a.AplicarDto ? a.DescuentoProducto : 0)} → {Porcentaje(b.AplicarDto ? b.DescuentoProducto : 0)}");
            }
            if (!IgualesPrecio(a.DescuentoCliente, b.DescuentoCliente))
            {
                d.Add($"descuento de cliente {Porcentaje(a.DescuentoCliente)} → {Porcentaje(b.DescuentoCliente)}");
            }
            if (!IgualesPrecio(a.DescuentoPP, b.DescuentoPP))
            {
                d.Add($"pronto pago {Porcentaje(a.DescuentoPP)} → {Porcentaje(b.DescuentoPP)}");
            }
            if (!IgualesPrecio(a.Descuento, b.Descuento))
            {
                d.Add($"descuento de línea {Porcentaje(a.Descuento)} → {Porcentaje(b.Descuento)}");
            }
            if (!string.Equals(Limpio(a.Iva), Limpio(b.Iva), StringComparison.OrdinalIgnoreCase)
                || a.PorcentajeIva != b.PorcentajeIva || !IgualesPrecio(a.PorcentajeRe, b.PorcentajeRe))
            {
                d.Add($"IVA {TextoIva(a)} → {TextoIva(b)}");
            }
            if (!IgualesImporte(a.BaseImponible, b.BaseImponible))
            {
                d.Add($"base {Importe(a.BaseImponible)} → {Importe(b.BaseImponible)}");
            }
            if (!IgualesImporte(a.ImporteIva, b.ImporteIva) || !IgualesImporte(a.ImporteRe, b.ImporteRe) || !IgualesImporte(a.Total, b.Total))
            {
                d.Add($"total {Importe(a.Total)} → {Importe(b.Total)}");
            }
            return d;
        }

        /// <summary>El 409 de usted: el pedido tiene picking y con el cliente nuevo cambiaría algo.</summary>
        public static string MotivoNoSePuede(int numero, string clienteNuevo, List<string> diferencias)
        {
            List<string> todas = diferencias ?? new List<string>();
            IEnumerable<string> nombradas = todas.Take(MAXIMO_LINEAS);
            string mas = todas.Count > MAXIMO_LINEAS ? $" y {todas.Count - MAXIMO_LINEAS} más" : "";
            return $"No se puede cambiar el cliente del pedido {numero}: ya tiene picking y con las condiciones del cliente {clienteNuevo?.Trim()} " +
                $"cambiaría: {string.Join("; ", nombradas)}{mas}. Con picking solo se cambia si no cambia nada; " +
                "copie antes al cliente nuevo las condiciones del anterior o quite el picking.";
        }

        /// <summary>La campana a Almacén: el pedido con picking ha pasado a otro cliente (el picking y las reservas siguen igual).</summary>
        public static NotificacionPushDTO Notificacion(string empresa, int numero, string clienteAnterior, string contactoAnterior,
            string clienteNuevo, string contactoNuevo, IEnumerable<int> pickings, string usuario)
        {
            List<int> lista = (pickings ?? Enumerable.Empty<int>()).Where(p => p != 0).Distinct().OrderBy(p => p).ToList();
            string textoPickings = lista.Count == 0 ? "su picking" : lista.Count == 1 ? $"el picking {lista[0]}" : $"los pickings {string.Join(", ", lista)}";
            string quien = string.IsNullOrWhiteSpace(usuario) ? "" : $" ({usuario.Substring(usuario.LastIndexOf('\\') + 1).Trim()})";
            return new NotificacionPushDTO
            {
                Titulo = $"El pedido {numero} ha pasado del cliente {clienteAnterior?.Trim()} al {clienteNuevo?.Trim()}",
                Cuerpo = $"El pedido {numero}, con {textoPickings}, es ahora del cliente {clienteNuevo?.Trim()}/{contactoNuevo?.Trim()} " +
                    $"(antes {clienteAnterior?.Trim()}/{contactoAnterior?.Trim()}){quien}. Precios e importes no han cambiado y el picking " +
                    "sigue igual. Si ya tienen impresos el albarán o la etiqueta con el cliente anterior, vuelvan a sacarlos.",
                Tipo = TIPO_AVISO,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_AVISO,
                    ["empresa"] = empresa?.Trim(),
                    ["pedido"] = numero.ToString(CultureInfo.InvariantCulture),
                    ["clienteAnterior"] = clienteAnterior?.Trim(),
                    ["cliente"] = clienteNuevo?.Trim(),
                    ["contacto"] = contactoNuevo?.Trim(),
                    ["pickings"] = string.Join(",", lista)
                }
            };
        }

        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        private static string Limpio(string valor) => string.IsNullOrWhiteSpace(valor) ? "" : valor.Trim();

        private static string Texto(string valor, string siVacio) => string.IsNullOrWhiteSpace(valor) ? siVacio : valor.Trim();

        private static string Importe(decimal importe) => importe.ToString("N2", ES) + " €";

        private static string Porcentaje(decimal tantoPorUno) => (tantoPorUno * 100).ToString("0.##", ES) + " %";

        private static string TextoIva(FotoLineaCambioCliente f)
        {
            string re = f.PorcentajeRe == 0 ? "" : $" + RE {(f.PorcentajeRe * 100).ToString("0.##", ES)} %";
            return $"{Texto(f.Iva, "sin IVA")} ({f.PorcentajeIva} %{re})";
        }
    }

    /// <summary>NestoAPI#876: lo de una línea que el cambio de cliente puede tocar (para comparar y para dejarla como estaba).</summary>
    public class FotoLineaCambioCliente
    {
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public decimal? Precio { get; set; }
        public decimal DescuentoCliente { get; set; }
        public decimal DescuentoProducto { get; set; }
        public decimal Descuento { get; set; }
        public decimal DescuentoPP { get; set; }
        public bool AplicarDto { get; set; }
        public string Iva { get; set; }
        public byte PorcentajeIva { get; set; }
        public decimal PorcentajeRe { get; set; }
        public decimal Bruto { get; set; }
        public decimal SumaDescuentos { get; set; }
        public decimal ImporteDto { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal ImporteIva { get; set; }
        public decimal ImporteRe { get; set; }
        public decimal Total { get; set; }

        public static FotoLineaCambioCliente De(LinPedidoVta l)
        {
            return new FotoLineaCambioCliente
            {
                NumeroOrden = l.Nº_Orden,
                Producto = l.Producto?.Trim(),
                Cliente = l.Nº_Cliente,
                Contacto = l.Contacto,
                Precio = l.Precio,
                DescuentoCliente = l.DescuentoCliente,
                DescuentoProducto = l.DescuentoProducto,
                Descuento = l.Descuento,
                DescuentoPP = l.DescuentoPP,
                AplicarDto = l.Aplicar_Dto,
                Iva = l.IVA,
                PorcentajeIva = l.PorcentajeIVA,
                PorcentajeRe = l.PorcentajeRE,
                Bruto = l.Bruto,
                SumaDescuentos = l.SumaDescuentos,
                ImporteDto = l.ImporteDto,
                BaseImponible = l.Base_Imponible,
                ImporteIva = l.ImporteIVA,
                ImporteRe = l.ImporteRE,
                Total = l.Total
            };
        }

        public void Restaurar(LinPedidoVta l)
        {
            l.Nº_Cliente = Cliente;
            l.Contacto = Contacto;
            l.Precio = Precio;
            l.DescuentoCliente = DescuentoCliente;
            l.DescuentoProducto = DescuentoProducto;
            l.Descuento = Descuento;
            l.DescuentoPP = DescuentoPP;
            l.Aplicar_Dto = AplicarDto;
            l.IVA = Iva;
            l.PorcentajeIVA = PorcentajeIva;
            l.PorcentajeRE = PorcentajeRe;
            l.Bruto = Bruto;
            l.SumaDescuentos = SumaDescuentos;
            l.ImporteDto = ImporteDto;
            l.Base_Imponible = BaseImponible;
            l.ImporteIVA = ImporteIva;
            l.ImporteRE = ImporteRe;
            l.Total = Total;
        }
    }

    /// <summary>NestoAPI#876: lo de la cabecera que el cambio de cliente toca (para el ensayo y para dejarla como estaba).</summary>
    public class FotoCabeceraCambioCliente
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string ContactoCobro { get; set; }
        public string Iva { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        public string Ccc { get; set; }
        public DateTime? PrimerVencimiento { get; set; }
        public bool VtoBuenoPlazosPago { get; set; }
        public string PeriodoFacturacion { get; set; }
        public string Ruta { get; set; }
        public string Vendedor { get; set; }
        public decimal NoComisiona { get; set; }
        public byte? ModoFacturacion { get; set; }
        public bool MantenerJunto { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaModificacion { get; set; }

        public static FotoCabeceraCambioCliente De(CabPedidoVta c)
        {
            return new FotoCabeceraCambioCliente
            {
                Cliente = c.Nº_Cliente,
                Contacto = c.Contacto,
                ContactoCobro = c.ContactoCobro,
                Iva = c.IVA,
                FormaPago = c.Forma_Pago,
                PlazosPago = c.PlazosPago,
                Ccc = c.CCC,
                PrimerVencimiento = c.Primer_Vencimiento,
                VtoBuenoPlazosPago = c.vtoBuenoPlazosPago,
                PeriodoFacturacion = c.Periodo_Facturacion,
                Ruta = c.Ruta,
                Vendedor = c.Vendedor,
                NoComisiona = c.NoComisiona,
                ModoFacturacion = c.ModoFacturacion,
                MantenerJunto = c.MantenerJunto,
                Usuario = c.Usuario,
                FechaModificacion = c.Fecha_Modificación
            };
        }

        public void Restaurar(CabPedidoVta c)
        {
            c.Nº_Cliente = Cliente;
            c.Contacto = Contacto;
            c.ContactoCobro = ContactoCobro;
            c.IVA = Iva;
            c.Forma_Pago = FormaPago;
            c.PlazosPago = PlazosPago;
            c.CCC = Ccc;
            c.Primer_Vencimiento = PrimerVencimiento;
            c.vtoBuenoPlazosPago = VtoBuenoPlazosPago;
            c.Periodo_Facturacion = PeriodoFacturacion;
            c.Ruta = Ruta;
            c.Vendedor = Vendedor;
            c.NoComisiona = NoComisiona;
            c.ModoFacturacion = ModoFacturacion;
            c.MantenerJunto = MantenerJunto;
            c.Usuario = Usuario;
            c.Fecha_Modificación = FechaModificacion;
        }

        /// <summary>Lo que cambia en la cabecera (informativo, en el ensayo), con las mismas etiquetas que DescribirCambios.</summary>
        public static List<string> Cambios(FotoCabeceraCambioCliente a, FotoCabeceraCambioCliente b)
        {
            List<string> c = new List<string>();
            Anotar(c, "Cliente", $"{a.Cliente?.Trim()}/{a.Contacto?.Trim()}", $"{b.Cliente?.Trim()}/{b.Contacto?.Trim()}");
            Anotar(c, "IVA", T(a.Iva, "sin IVA"), T(b.Iva, "sin IVA"));
            Anotar(c, "Forma de pago", T(a.FormaPago), T(b.FormaPago));
            Anotar(c, "Plazos de pago", T(a.PlazosPago), T(b.PlazosPago));
            Anotar(c, "CCC", T(a.Ccc, "sin CCC"), T(b.Ccc, "sin CCC"));
            Anotar(c, "Vendedor", T(a.Vendedor), T(b.Vendedor));
            Anotar(c, "Ruta", T(a.Ruta), T(b.Ruta));
            Anotar(c, "Periodo de facturación", T(a.PeriodoFacturacion), T(b.PeriodoFacturacion));
            Anotar(c, "Facturación",
                Constantes.Pedidos.ModosFacturacion.Nombre(Constantes.Pedidos.ModosFacturacion.Efectivo(a.ModoFacturacion, a.MantenerJunto)),
                Constantes.Pedidos.ModosFacturacion.Nombre(Constantes.Pedidos.ModosFacturacion.Efectivo(b.ModoFacturacion, b.MantenerJunto)));
            return c;
        }

        private static void Anotar(List<string> cambios, string que, string antes, string despues)
        {
            if (!string.Equals(antes, despues, StringComparison.Ordinal))
            {
                cambios.Add($"{que}: {antes} → {despues}");
            }
        }

        private static string T(string valor, string siVacio = "(vacío)") => string.IsNullOrWhiteSpace(valor) ? siVacio : valor.Trim();
    }
}
