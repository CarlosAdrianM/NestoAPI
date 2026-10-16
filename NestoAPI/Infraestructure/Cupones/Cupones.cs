using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;

namespace NestoAPI.Infraestructure.Cupones
{
    /// <summary>NestoAPI#613: estados de un cupón (Cupones.Estado).</summary>
    public static class EstadosCupon
    {
        /// <summary>Emitido antes del día del evento: el cliente ya lo ve, pero todavía no vale.</summary>
        public const string EMITIDO = "Emitido";
        /// <summary>Se puede usar (desde el día del evento hasta la caducidad).</summary>
        public const string ACTIVO = "Activo";
        /// <summary>Aplicado en un pedido (UsadoEnPedido). Si se quita la línea o se borra el pedido antes de servirlo, vuelve.</summary>
        public const string USADO = "Usado";
        /// <summary>No asistió al evento, o anulado a mano.</summary>
        public const string ANULADO = "Anulado";
        /// <summary>Pasó la caducidad sin usarse.</summary>
        public const string CADUCADO = "Caducado";
    }

    public static class OrigenesCupon
    {
        public const string EVENTO = "Evento";
        public const string MANUAL = "Manual";
    }

    /// <summary>NestoAPI#613: un cupón tal como sale de la tabla, con lo que hace falta para decidir si se puede usar.</summary>
    public class CuponLeido
    {
        public int Id { get; set; }
        public string Codigo { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>IVA incluido.</summary>
        public decimal Importe { get; set; }
        public string Origen { get; set; }
        public int? EventoInscripcionId { get; set; }
        public int? EventoId { get; set; }
        public string Evento { get; set; }
        public string EmpresaFacturaOrigen { get; set; }
        public string FacturaOrigen { get; set; }
        public string Motivo { get; set; }
        public DateTime FechaEmision { get; set; }
        public DateTime ValidoDesde { get; set; }
        public DateTime Caducidad { get; set; }
        public string Estado { get; set; }
        public string EmpresaUso { get; set; }
        public int? UsadoEnPedido { get; set; }
        public string EmpresaFacturaUso { get; set; }
        public string FacturaUso { get; set; }
        public DateTime? FechaUso { get; set; }
        public string CanalUso { get; set; }
        public string MotivoAnulacion { get; set; }
        public string Usuario { get; set; }
        /// <summary>El pedido donde está usado todavía tiene la línea del cupón. Si no (se borró fuera de la API), el cupón
        /// está libre aunque ponga Usado: se cura solo al volver a usarlo.</summary>
        public bool PedidoUsoTieneLaLinea { get; set; }
        /// <summary>La línea del cupón del pedido de uso ya está en albarán o factura (ya no se suelta).</summary>
        public bool LineaUsoServida { get; set; }
    }

    /// <summary>NestoAPI#613: lo que ven Nesto, NestoApp y TNV.</summary>
    public class CuponDTO
    {
        public int Id { get; set; }
        public string Codigo { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>IVA incluido: lo que se descuenta del pedido.</summary>
        public decimal Importe { get; set; }
        /// <summary>El pedido tiene que llevar al menos esto de productos (IVA incluido): el doble del cupón.</summary>
        public decimal PedidoMinimo { get; set; }
        public string Origen { get; set; }
        public int? EventoId { get; set; }
        public string Evento { get; set; }
        public string Motivo { get; set; }
        public DateTime FechaEmision { get; set; }
        public DateTime ValidoDesde { get; set; }
        public DateTime Caducidad { get; set; }
        /// <summary>Emitido, Activo, Usado, Anulado o Caducado (el de hoy, aunque el job no haya pasado todavía).</summary>
        public string Estado { get; set; }
        public bool SePuedeUsar { get; set; }
        /// <summary>Frase para la plantilla («Tiene un cupón de 50,00 €…»).</summary>
        public string Mensaje { get; set; }
        /// <summary>El producto de la línea del cupón (para mandarla con CodigoCupon).</summary>
        public string Producto { get; set; }
        public string TextoLinea { get; set; }
        public int? UsadoEnPedido { get; set; }
        public string FacturaUso { get; set; }
        public DateTime? FechaUso { get; set; }
        public string FacturaOrigen { get; set; }
    }

    /// <summary>NestoAPI#613: POST api/Cupones (cupón manual, solo Dirección).</summary>
    public class CrearCuponManualDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>IVA incluido.</summary>
        public decimal Importe { get; set; }
        /// <summary>Obligatorio.</summary>
        public string Motivo { get; set; }
        /// <summary>Opcional: si no viene, hoy.</summary>
        public DateTime? ValidoDesde { get; set; }
    }

    /// <summary>Lo que hace falta de la ficha del producto para el mínimo del cupón.</summary>
    public class ProductoParaCupon
    {
        public string Numero { get; set; }
        public string Grupo { get; set; }
        public bool Ficticio { get; set; }
        public string IvaRepercutido { get; set; }
    }

    /// <summary>
    /// NestoAPI#613: los porcentajes de IVA y de recargo de equivalencia (en tanto por cien, como ParametrosIVA: 21 y 5,20) con
    /// los que se saca la base de la línea del cupón para que descuente exactamente su importe al cliente (Carlos, 15/10/26).
    /// </summary>
    public class PorcentajesIvaCupon
    {
        public decimal Iva { get; set; }
        public decimal RecargoEquivalencia { get; set; }

        /// <summary>Lo de siempre si no se encuentra la combinación en ParametrosIVA: 21 % sin recargo.</summary>
        public static PorcentajesIvaCupon PorDefecto => new PorcentajesIvaCupon { Iva = ReglasCupon.PORCENTAJE_IVA, RecargoEquivalencia = 0 };
        /// <summary>Pedido sin IVA (la cabecera no lo lleva): la línea no lleva impuestos, así que la base es el importe.</summary>
        public static PorcentajesIvaCupon SinIva => new PorcentajesIvaCupon { Iva = 0, RecargoEquivalencia = 0 };
    }

    /// <summary>
    /// NestoAPI#613: las reglas del cupón, todas aquí y sin base de datos (decisiones de la épica #610, 08/10/26): de una vez,
    /// uno por pedido, pedido mínimo el doble del cupón IVA incluido sin contar cursos ni portes, solo productos, convive con
    /// ofertas y descuentos (va sobre el neto), válido desde el día del evento y durante 6 meses, del mismo cliente.
    /// </summary>
    public static class ReglasCupon
    {
        /// <summary>
        /// El cupón es un descuento comercial en producto al 21 %: 50 € → base −41,32 (Carlos, 08/10/26). Con recargo de
        /// equivalencia la base sale del IVA + RE del cliente, para que descuente siempre el importe exacto (Carlos, 15/10/26).
        /// </summary>
        public const decimal PORCENTAJE_IVA = 21M;
        public const int MESES_VALIDEZ = 6;
        public const decimal MULTIPLO_PEDIDO_MINIMO = 2M;
        public const string CLAVE_PRODUCTO = "Cupones:Producto";
        /// <summary>El producto ficticio de la línea de descuento (script Issue613_Cupones.sql).</summary>
        public const string PRODUCTO_POR_DEFECTO = "CUPONEVENTO";
        public const int LONGITUD_TEXTO_LINEA = 50;

        public const string ERROR_NO_EXISTE = "CUPON_NO_EXISTE";
        public const string ERROR_SIN_CODIGO = "CUPON_SIN_CODIGO";
        public const string ERROR_OTRO_CLIENTE = "CUPON_OTRO_CLIENTE";
        public const string ERROR_TODAVIA_NO = "CUPON_TODAVIA_NO_VALE";
        public const string ERROR_CADUCADO = "CUPON_CADUCADO";
        public const string ERROR_ANULADO = "CUPON_ANULADO";
        public const string ERROR_YA_USADO = "CUPON_YA_USADO";
        public const string ERROR_VARIOS = "CUPON_VARIOS_EN_EL_PEDIDO";
        public const string ERROR_MINIMO = "CUPON_PEDIDO_MINIMO";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
        private const string ALFABETO_CODIGO = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        public static string ProductoCupon
        {
            get
            {
                string valor = ConfigurationManager.AppSettings[CLAVE_PRODUCTO]?.Trim();
                return string.IsNullOrEmpty(valor) ? PRODUCTO_POR_DEFECTO : valor;
            }
        }

        public static bool EsProductoCupon(string producto, string productoCupon) =>
            !string.IsNullOrWhiteSpace(producto) && string.Equals(producto.Trim(), productoCupon, StringComparison.OrdinalIgnoreCase);

        public static bool EsLineaCupon(byte? tipoLinea, string producto, string productoCupon) =>
            (tipoLinea == null || tipoLinea == Constantes.TiposLineaVenta.PRODUCTO) && EsProductoCupon(producto, productoCupon);

        /// <summary>Precio (base) de la línea −1 al 21 % sin recargo: el importe IVA incluido entre 1,21.</summary>
        public static decimal PrecioLinea(decimal importeIvaIncluido) => PrecioLinea(importeIvaIncluido, PorcentajesIvaCupon.PorDefecto);

        /// <summary>
        /// Precio (base) de la línea −1, a 4 decimales como LinPedidoVta.Precio: el importe entre (1 + IVA + RE) del cliente, para
        /// que el cupón descuente siempre su importe exacto (Carlos, 15/10/26): 50 € con RE (21 % + 5,2 %) → base 39,62, no 41,32
        /// (que serían 52,15 €). Sin porcentajes, los de siempre (21 %).
        /// </summary>
        public static decimal PrecioLinea(decimal importe, PorcentajesIvaCupon porcentajes)
        {
            PorcentajesIvaCupon p = porcentajes ?? PorcentajesIvaCupon.PorDefecto;
            return Math.Round(importe / (1 + (p.Iva + p.RecargoEquivalencia) / 100M), 4, MidpointRounding.AwayFromZero);
        }

        public static decimal PedidoMinimo(decimal importe) => Math.Round(importe * MULTIPLO_PEDIDO_MINIMO, 2, MidpointRounding.AwayFromZero);

        public static DateTime Caducidad(DateTime validoDesde) => validoDesde.Date.AddMonths(MESES_VALIDEZ);

        /// <summary>«EV-2610-7K3Q»: prefijo (EV de evento, MN de manual), año y mes, y cuatro caracteres sin ambigüedades (ni 0/O ni 1/I).</summary>
        public static string NuevoCodigo(string origen, DateTime fecha, Func<int, int> aleatorio = null)
        {
            Func<int, int> azar = aleatorio ?? AleatorioSeguro;
            string prefijo = origen == OrigenesCupon.MANUAL ? "MN" : "EV";
            var sufijo = new char[4];
            for (int i = 0; i < sufijo.Length; i++)
            {
                sufijo[i] = ALFABETO_CODIGO[azar(ALFABETO_CODIGO.Length)];
            }
            return $"{prefijo}-{fecha:yyMM}-{new string(sufijo)}";
        }

        private static int AleatorioSeguro(int maximo)
        {
            byte[] b = new byte[4];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(b);
            }
            return (int)(BitConverter.ToUInt32(b, 0) % (uint)maximo);
        }

        /// <summary>El estado de hoy: el job lo pone cada mañana, pero un Emitido cuyo día ha llegado ya vale y uno caducado ya no.</summary>
        public static string EstadoEfectivo(CuponLeido c, DateTime hoy)
        {
            string estado = c.Estado?.Trim();
            if (estado == EstadosCupon.USADO && !c.PedidoUsoTieneLaLinea)
            {
                estado = EstadosCupon.ACTIVO; // su pedido ya no lo tiene: libre
            }
            if (estado == EstadosCupon.USADO || estado == EstadosCupon.ANULADO || estado == EstadosCupon.CADUCADO)
            {
                return estado;
            }
            if (hoy.Date > c.Caducidad.Date)
            {
                return EstadosCupon.CADUCADO;
            }
            return hoy.Date < c.ValidoDesde.Date ? EstadosCupon.EMITIDO : EstadosCupon.ACTIVO;
        }

        public static string TextoLinea(CuponLeido c)
        {
            string evento = string.IsNullOrWhiteSpace(c.Evento) ? null : c.Evento.Trim();
            string texto = evento == null ? $"Cupón {c.Codigo?.Trim()}" : $"Cupón {c.Codigo?.Trim()} ({evento})";
            return texto.Length > LONGITUD_TEXTO_LINEA ? texto.Substring(0, LONGITUD_TEXTO_LINEA) : texto;
        }

        /// <summary>
        /// La línea del cupón la pone el servidor, venga como venga: producto del cupón, cantidad −1, precio = importe / (1 + IVA + RE),
        /// sin descuentos de línea, de producto ni de cliente (el pronto pago de la cabecera sí le aplica, como al cheque regalo
        /// y a toda la factura) y el IVA del producto.
        /// </summary>
        public static void Normalizar(LineaPedidoVentaDTO linea, CuponLeido cupon, string productoCupon, string ivaProducto, bool pedidoConIva,
            PorcentajesIvaCupon porcentajes = null)
        {
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = productoCupon;
            linea.Cantidad = -1;
            linea.DescuentoLinea = 0;
            linea.DescuentoProducto = 0;
            linea.DescuentoEntidad = 0;
            linea.AplicarDescuento = false;
            linea.oferta = null;
            linea.EsFicticio = true;
            linea.EsCupon = true;
            if (cupon != null)
            {
                linea.PrecioUnitario = PrecioLinea(cupon.Importe, porcentajes);
                linea.texto = TextoLinea(cupon);
            }
            if (pedidoConIva && !string.IsNullOrWhiteSpace(ivaProducto))
            {
                linea.iva = ivaProducto.Trim();
            }
        }

        /// <summary>Lo mismo sobre la línea ya creada, mientras se puede tocar. Devuelve si hay que recalcular importes.</summary>
        public static bool Normalizar(LinPedidoVta linea, CuponLeido cupon, string ivaProducto, bool pedidoConIva, PorcentajesIvaCupon porcentajes = null)
        {
            if ((linea.Picking ?? 0) != 0 || linea.Estado > Constantes.EstadosLineaVenta.EN_CURSO)
            {
                return false;
            }
            bool cambia = false;
            decimal precio = PrecioLinea(cupon.Importe, porcentajes);
            string texto = TextoLinea(cupon);
            if (linea.Cantidad != -1) { linea.Cantidad = -1; cambia = true; }
            if (linea.Precio != precio) { linea.Precio = precio; cambia = true; }
            if (linea.Descuento != 0) { linea.Descuento = 0; cambia = true; }
            if (linea.DescuentoProducto != 0) { linea.DescuentoProducto = 0; cambia = true; }
            if (linea.Aplicar_Dto) { linea.Aplicar_Dto = false; cambia = true; }
            if (linea.NºOferta != null) { linea.NºOferta = null; cambia = true; }
            if (linea.Texto?.Trim() != texto) { linea.Texto = texto; cambia = true; }
            if (pedidoConIva && !string.IsNullOrWhiteSpace(ivaProducto) && linea.IVA?.Trim() != ivaProducto.Trim())
            {
                linea.IVA = ivaProducto.Trim();
                cambia = true;
            }
            return cambia;
        }

        /// <summary>
        /// ¿Suma para el pedido mínimo? Solo productos de verdad: ni cuentas contables (portes, reembolso…), ni ficticios
        /// (cursos, el propio cupón, el cheque regalo), ni el grupo de cursos.
        /// </summary>
        public static bool ComputaParaMinimo(byte? tipoLinea, ProductoParaCupon producto)
        {
            if (tipoLinea != Constantes.TiposLineaVenta.PRODUCTO || producto == null || producto.Ficticio)
            {
                return false;
            }
            return !string.Equals(producto.Grupo?.Trim(), Constantes.Productos.GRUPO_CURSOS, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Total IVA incluido (después de descuentos: el cupón va sobre el neto) de lo que suma para el mínimo.</summary>
        public static decimal TotalComputable(IEnumerable<LinPedidoVta> lineas, IDictionary<string, ProductoParaCupon> productos) =>
            Math.Round(lineas.Where(l => ComputaParaMinimo(l.TipoLinea, Buscar(productos, l.Producto))).Sum(l => l.Total), 2, MidpointRounding.AwayFromZero);

        internal static ProductoParaCupon Buscar(IDictionary<string, ProductoParaCupon> productos, string producto) =>
            producto != null && productos != null && productos.TryGetValue(producto.Trim(), out ProductoParaCupon p) ? p : null;

        /// <summary>
        /// Las reglas duras de usar <paramref name="cupon"/> en el pedido. <paramref name="yaEnEstePedido"/>: ya estaba aplicado
        /// en este pedido (un PUT posterior): entonces no se vuelven a mirar las fechas ni el estado, pero sí el cliente y el mínimo.
        /// </summary>
        public static void Comprobar(CuponLeido cupon, string empresa, int pedido, string cliente, DateTime hoy, bool yaEnEstePedido,
            decimal totalComputable)
        {
            string clienteTrim = cliente?.Trim();
            string codigo = cupon.Codigo?.Trim();
            if (!string.Equals(cupon.Cliente?.Trim(), clienteTrim, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(cupon.Empresa?.Trim(), empresa?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw Error(ERROR_OTRO_CLIENTE, $"El cupón {codigo} es de otro cliente: solo lo puede usar el cliente {cupon.Cliente?.Trim()}.",
                    empresa, pedido, cliente);
            }
            if (!yaEnEstePedido)
            {
                string estado = EstadoEfectivo(cupon, hoy);
                switch (estado)
                {
                    case EstadosCupon.ANULADO:
                        throw Error(ERROR_ANULADO, $"El cupón {codigo} está anulado" +
                            (string.IsNullOrWhiteSpace(cupon.MotivoAnulacion) ? "." : $": {cupon.MotivoAnulacion.Trim()}."), empresa, pedido, cliente);
                    case EstadosCupon.CADUCADO:
                        throw Error(ERROR_CADUCADO, $"El cupón {codigo} se podía usar hasta el {Fecha(cupon.Caducidad)}.", empresa, pedido, cliente);
                    case EstadosCupon.USADO:
                        throw Error(ERROR_YA_USADO, $"El cupón {codigo} ya está aplicado en el pedido {cupon.UsadoEnPedido}: solo se puede usar una vez.",
                            empresa, pedido, cliente, new Dictionary<string, object> { ["pedidoUso"] = cupon.UsadoEnPedido });
                    case EstadosCupon.EMITIDO:
                        throw Error(ERROR_TODAVIA_NO, $"El cupón {codigo} vale desde el {Fecha(cupon.ValidoDesde)}.", empresa, pedido, cliente);
                }
            }
            decimal minimo = PedidoMinimo(cupon.Importe);
            if (totalComputable < minimo)
            {
                decimal falta = minimo - totalComputable;
                throw Error(ERROR_MINIMO, $"Para usar el cupón {codigo} de {Euros(cupon.Importe)} € el pedido tiene que ser de al menos " +
                    $"{Euros(minimo)} € de productos (IVA incluido, sin contar cursos ni portes). Lleva {Euros(totalComputable)} €: " +
                    $"faltan {Euros(falta)} €.", empresa, pedido, cliente,
                    new Dictionary<string, object> { ["minimo"] = minimo, ["totalComputable"] = totalComputable, ["falta"] = falta });
            }
        }

        public static string Mensaje(CuponLeido c, string estado)
        {
            switch (estado)
            {
                case EstadosCupon.ACTIVO:
                    return $"Tiene un cupón de {Euros(c.Importe)} € ({c.Codigo?.Trim()}) para un pedido de al menos {Euros(PedidoMinimo(c.Importe))} € " +
                        $"de productos, hasta el {Fecha(c.Caducidad)}.";
                case EstadosCupon.EMITIDO:
                    return $"Tiene un cupón de {Euros(c.Importe)} € ({c.Codigo?.Trim()}) que vale desde el {Fecha(c.ValidoDesde)} hasta el {Fecha(c.Caducidad)}.";
                case EstadosCupon.USADO:
                    return $"El cupón {c.Codigo?.Trim()} ya está aplicado en el pedido {c.UsadoEnPedido}.";
                case EstadosCupon.CADUCADO:
                    return $"El cupón {c.Codigo?.Trim()} se podía usar hasta el {Fecha(c.Caducidad)}.";
                default:
                    return $"El cupón {c.Codigo?.Trim()} está anulado.";
            }
        }

        public static CuponDTO ADTO(CuponLeido c, DateTime hoy, string productoCupon)
        {
            string estado = EstadoEfectivo(c, hoy);
            return new CuponDTO
            {
                Id = c.Id,
                Codigo = c.Codigo?.Trim(),
                Empresa = c.Empresa?.Trim(),
                Cliente = c.Cliente?.Trim(),
                Contacto = c.Contacto?.Trim(),
                Importe = c.Importe,
                PedidoMinimo = PedidoMinimo(c.Importe),
                Origen = c.Origen?.Trim(),
                EventoId = c.EventoId,
                Evento = c.Evento?.Trim(),
                Motivo = c.Motivo,
                FechaEmision = c.FechaEmision,
                ValidoDesde = c.ValidoDesde.Date,
                Caducidad = c.Caducidad.Date,
                Estado = estado,
                SePuedeUsar = estado == EstadosCupon.ACTIVO,
                Mensaje = Mensaje(c, estado),
                Producto = productoCupon,
                TextoLinea = TextoLinea(c),
                UsadoEnPedido = estado == EstadosCupon.USADO ? c.UsadoEnPedido : null,
                FacturaUso = c.FacturaUso?.Trim(),
                FechaUso = estado == EstadosCupon.USADO ? c.FechaUso : null,
                FacturaOrigen = c.FacturaOrigen?.Trim()
            };
        }

        internal static string Euros(decimal importe) => importe.ToString("N2", es);

        internal static string Fecha(DateTime fecha) => fecha.ToString("dd/MM/yyyy", es);

        internal static NestoBusinessException Error(string codigo, string mensaje, string empresa, int pedido, string cliente,
            Dictionary<string, object> datos = null)
        {
            var contexto = new ErrorContext
            {
                ErrorCode = codigo,
                Empresa = empresa?.Trim(),
                Pedido = pedido == 0 ? (int?)null : pedido,
                Cliente = cliente?.Trim()
            };
            if (datos != null)
            {
                foreach (KeyValuePair<string, object> dato in datos)
                {
                    contexto.AdditionalData[dato.Key] = dato.Value;
                }
            }
            return new NestoBusinessException(mensaje, contexto);
        }
    }
}
