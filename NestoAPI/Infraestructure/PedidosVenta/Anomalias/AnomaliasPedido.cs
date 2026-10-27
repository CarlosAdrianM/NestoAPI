using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;

namespace NestoAPI.Infraestructure.PedidosVenta.Anomalias
{
    /// <summary>
    /// NestoAPI#715: el interruptor <c>Anomalias:Modo</c> del Web.config. Sin clave o con cualquier otro valor, Apagado (no se
    /// consulta nada al grabar). Sombra: se evalúan las reglas y se registra en PedidosAnomalias, pero el cliente no recibe nada.
    /// Activo (corte 4): además se pregunta al usuario (400 ANOMALIA_PEDIDO con el flag ConfirmarAnomalias); en el corte 1 se
    /// comporta como Sombra.
    /// </summary>
    public enum ModoAnomalias
    {
        Apagado,
        Sombra,
        Activo
    }

    public static class InterruptorAnomalias
    {
        public const string CLAVE = "Anomalias:Modo";

        /// <summary>Para los tests (null = el Web.config).</summary>
        internal static Func<string, string> LeerAjuste { get; set; }

        public static ModoAnomalias Modo => Interpretar(Leer(CLAVE));

        internal static ModoAnomalias Interpretar(string valor)
        {
            switch (valor?.Trim().ToUpperInvariant())
            {
                case "SOMBRA":
                    return ModoAnomalias.Sombra;
                case "ACTIVO":
                    return ModoAnomalias.Activo;
                default:
                    return ModoAnomalias.Apagado;
            }
        }

        private static string Leer(string clave)
        {
            try
            {
                return LeerAjuste != null ? LeerAjuste(clave) : ConfigurationManager.AppSettings[clave];
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>NestoAPI#715: los valores de la columna Regla de PedidosAnomalias.</summary>
    public static class ReglasAnomalia
    {
        /// <summary>La fila de contexto del pedido (una por grabación, sin señal): aplicación, deuda, forma de pago, importe.</summary>
        public const string CONTEXTO = "Contexto";
        /// <summary>Cantidad mayor que max(3 × mediana del cliente, p99 de todos los clientes).</summary>
        public const string CANTIDAD_ALTA = "CantidadAlta";
        /// <summary>Cantidad mayor que 5 × el máximo anterior del cliente.</summary>
        public const string CANTIDAD_VS_MAXIMO = "CantidadVsMaximo";
        /// <summary>Precio menor que 0,5 × PrecioTarifa.</summary>
        public const string PRECIO_BAJO = "PrecioBajo";
        /// <summary>Precio mayor que 1,5 × el habitual del cliente (o la tarifa si no lo ha comprado).</summary>
        public const string PRECIO_ALTO = "PrecioAlto";
        /// <summary>Descuento mayor que el máximo habitual + 10 puntos y fuera de lo permitido.</summary>
        public const string DESCUENTO_ALTO = "DescuentoAlto";
        /// <summary>Importe del pedido mayor que 4 × la media del cliente.</summary>
        public const string IMPORTE_PEDIDO = "ImportePedido";
        /// <summary>Grupo que el cliente nunca ha comprado, con cantidad alta.</summary>
        public const string GRUPO_NUEVO = "GrupoNuevo";
        /// <summary>#716 2.10: la línea se graba con más cantidad que stock en su almacén (demanda insatisfecha).</summary>
        public const string SIN_STOCK = "SinStock";

        /// <summary>VersionModelo de las filas que ponen las reglas (el modelo del corte 3 pondrá la suya).</summary>
        public const string VERSION_REGLAS = "reglas-v1";
    }

    /// <summary>NestoAPI#715: los valores de la columna Resultado (el etiquetado nocturno).</summary>
    public static class ResultadosAnomalia
    {
        public const string CORREGIDO_48H = "CorregidoEn48h";
        public const string ABONADO = "Abonado";
        public const string SIN_CAMBIOS = "SinCambios";
    }

    public static class OperacionesAnomalia
    {
        public const string CREAR = "Crear";
        public const string MODIFICAR = "Modificar";
    }

    /// <summary>
    /// NestoAPI#715 (#716 2.5): con qué aplicación se graba el pedido. Si el cliente manda la cabecera <c>X-Aplicacion</c>, esa (en
    /// oct/26 ninguno la manda todavía). Si no, se infiere de cómo se autentica cada aplicación (CLAUDE.md, «Flujos de
    /// autenticación»): TiendasNuevaVision graba con el usuario <c>APP\cliente</c> en el pedido y su token lleva el email; Nesto
    /// usa api/auth/windows-token y su principal es <c>DOMINIO\usuario</c>; NestoApp usa /oauth/token con el UserName de Identity
    /// (sin dominio ni arroba).
    /// </summary>
    public static class AplicacionesOrigen
    {
        public const string CABECERA_HTTP = "X-Aplicacion";
        public const string NESTO = "Nesto";
        public const string NESTO_APP = "NestoApp";
        public const string TIENDAS_NUEVA_VISION = "TiendasNuevaVision";
        public const string DESCONOCIDA = "Desconocida";
        public const int LARGO_MAXIMO = 20;

        public static string DesdeCabecera(HttpRequestMessage request)
        {
            try
            {
                if (request?.Headers != null && request.Headers.TryGetValues(CABECERA_HTTP, out IEnumerable<string> valores))
                {
                    return valores?.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
                }
            }
            catch
            {
                // La cabecera es opcional: cualquier fallo al leerla es como no mandarla
            }
            return null;
        }

        public static string Inferir(string cabecera, string usuarioPedido, string nombrePrincipal)
        {
            if (!string.IsNullOrWhiteSpace(cabecera))
            {
                string limpia = cabecera.Trim();
                return limpia.Length > LARGO_MAXIMO ? limpia.Substring(0, LARGO_MAXIMO) : limpia;
            }
            if (usuarioPedido != null && usuarioPedido.TrimStart().StartsWith(Constantes.FormasVenta.APP + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return TIENDAS_NUEVA_VISION;
            }
            string principal = nombrePrincipal?.Trim();
            if (!string.IsNullOrEmpty(principal))
            {
                if (principal.Contains("\\"))
                {
                    return NESTO;
                }
                return principal.Contains("@") ? TIENDAS_NUEVA_VISION : NESTO_APP;
            }
            string usuario = usuarioPedido?.Trim();
            if (!string.IsNullOrEmpty(usuario) && usuario.Contains("\\"))
            {
                return NESTO;
            }
            return DESCONOCIDA;
        }
    }

    /// <summary>NestoAPI#715: una señal de anomalía (una regla que salta en una línea o en el pedido).</summary>
    public class SenalAnomalia
    {
        public string Regla { get; set; }
        /// <summary>Nº de orden de la línea (0 en las nuevas, que aún no lo tienen) o null en las del pedido.</summary>
        public int? Linea { get; set; }
        public string Producto { get; set; }
        public decimal Valor { get; set; }
        public decimal Esperado { get; set; }
        /// <summary>Cuántas veces supera el umbral (Valor / Esperado; en el precio bajo, Esperado / Valor). Tope 99.</summary>
        public decimal Puntuacion { get; set; }

        public static decimal Puntuar(decimal valor, decimal esperado)
        {
            if (esperado <= 0)
            {
                return 99;
            }
            decimal puntos = Math.Round(valor / esperado, 4);
            return puntos > 99 ? 99 : puntos;
        }
    }

    /// <summary>NestoAPI#715: lo que el cliente ha comprado de un producto en los últimos 24 meses (líneas de producto con cantidad positiva).</summary>
    public class HistoricoProductoCliente
    {
        public string Producto { get; set; }
        public List<int> Cantidades { get; } = new List<int>();
        /// <summary>Precios unitarios de las líneas (los 0 no cuentan como habitual: regalos).</summary>
        public List<decimal> Precios { get; } = new List<decimal>();
        /// <summary>Descuentos de línea sin pronto pago (fracción).</summary>
        public List<decimal> Descuentos { get; } = new List<decimal>();
        /// <summary>Percentil 99 de la cantidad por línea entre todos los clientes (24 meses). Null sin ventas.</summary>
        public int? P99Todos { get; set; }
        public decimal? PrecioTarifa { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }

        public int Compras => Cantidades.Count;
        public decimal? MedianaCantidad => Mediana(Cantidades.Select(c => (decimal)c).ToList());
        public int? MaximoCantidad => Cantidades.Any() ? Cantidades.Max() : (int?)null;
        public decimal? PrecioHabitual => Mediana(Precios.Where(p => p > 0).ToList());
        public decimal? DescuentoMaximo => Descuentos.Any() ? Descuentos.Max() : (decimal?)null;

        internal static decimal? Mediana(List<decimal> valores)
        {
            if (valores == null || valores.Count == 0)
            {
                return null;
            }
            List<decimal> ordenados = valores.OrderBy(v => v).ToList();
            int mitad = ordenados.Count / 2;
            return ordenados.Count % 2 == 1 ? ordenados[mitad] : (ordenados[mitad - 1] + ordenados[mitad]) / 2;
        }
    }

    /// <summary>NestoAPI#715: el histórico del cliente (24 meses) que necesitan las reglas.</summary>
    public class HistoricoCliente
    {
        /// <summary>Pedidos del cliente en 24 meses (sin el que se graba).</summary>
        public int Pedidos { get; set; }
        /// <summary>Base imponible media de esos pedidos.</summary>
        public decimal ImporteMedio { get; set; }
        public HashSet<string> Grupos { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HistoricoProductoCliente> Productos { get; } = new Dictionary<string, HistoricoProductoCliente>(StringComparer.OrdinalIgnoreCase);

        public HistoricoProductoCliente De(string producto)
        {
            string clave = producto?.Trim() ?? string.Empty;
            if (!Productos.TryGetValue(clave, out HistoricoProductoCliente historico))
            {
                historico = new HistoricoProductoCliente { Producto = clave };
                Productos[clave] = historico;
            }
            return historico;
        }
    }

    /// <summary>NestoAPI#715 (#716 2.5, 2.6, 2.10): el contexto del cliente en el momento de grabar.</summary>
    public class ContextoGrabacion
    {
        public decimal? DeudaTotal { get; set; }
        public decimal? DeudaVencida { get; set; }
        public int? DiasVencidoMasAntiguo { get; set; }
        public string FormaPagoFicha { get; set; }
        /// <summary>Stock por «producto|almacén» (clave <see cref="Clave"/>). Sin entrada = sin movimientos = 0; null en el diccionario no se usa.</summary>
        public Dictionary<string, int> Stock { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Si la lectura del stock falló o no se pidió: StockAlGrabar queda NULL.</summary>
        public bool StockLeido { get; set; }

        public static string Clave(string producto, string almacen) => (producto?.Trim() ?? string.Empty) + "|" + (almacen?.Trim() ?? string.Empty);

        public int? StockDe(string producto, string almacen)
        {
            if (!StockLeido)
            {
                return null;
            }
            return Stock.TryGetValue(Clave(producto, almacen), out int stock) ? stock : 0;
        }
    }

    /// <summary>NestoAPI#715: una línea tal y como estaba grabada antes de un PUT (para evaluar solo lo nuevo o lo que cambia).</summary>
    public class LineaGrabadaAnomalia
    {
        public int Linea { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public decimal Precio { get; set; }
        /// <summary>Descuento sin pronto pago (fracción).</summary>
        public decimal Descuento { get; set; }

        public static LineaGrabadaAnomalia Desde(LinPedidoVta linea)
        {
            if (linea == null)
            {
                return null;
            }
            return new LineaGrabadaAnomalia
            {
                Linea = linea.Nº_Orden,
                Producto = linea.Producto?.Trim(),
                Cantidad = linea.Cantidad ?? 0,
                Precio = linea.Precio ?? 0,
                Descuento = linea.Aplicar_Dto
                    ? 1 - (1 - linea.DescuentoCliente) * (1 - linea.DescuentoProducto) * (1 - linea.Descuento)
                    : linea.Descuento
            };
        }
    }

    /// <summary>NestoAPI#715: una línea del pedido que se graba, reducida a lo que miran las reglas.</summary>
    public class LineaInstantaneaAnomalia
    {
        public int Linea { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public decimal Precio { get; set; }
        /// <summary>Descuento sin pronto pago (fracción).</summary>
        public decimal Descuento { get; set; }
        public decimal PrecioTarifa { get; set; }
        public int? Oferta { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public string Almacen { get; set; }
        /// <summary>Cheque regalo, cupón, ficticia, bonificado Ganavisiones o no producto: no se evalúa.</summary>
        public bool Excluida { get; set; }

        public static LineaInstantaneaAnomalia Desde(LineaPedidoVentaDTO linea)
        {
            if (linea == null)
            {
                return null;
            }
            byte tipo = linea.tipoLinea ?? Constantes.TiposLineaVenta.PRODUCTO;
            return new LineaInstantaneaAnomalia
            {
                Linea = linea.id,
                Producto = linea.Producto?.Trim(),
                Cantidad = linea.Cantidad,
                Precio = linea.PrecioUnitario,
                Descuento = linea.SumaDescuentosSinPP,
                PrecioTarifa = linea.precioTarifa,
                Oferta = linea.oferta,
                Grupo = linea.GrupoProducto?.Trim(),
                SubGrupo = linea.SubgrupoProducto?.Trim(),
                Almacen = linea.almacen?.Trim(),
                Excluida = tipo != Constantes.TiposLineaVenta.PRODUCTO || string.IsNullOrWhiteSpace(linea.Producto)
                    || linea.EsChequeRegalo || linea.EsCupon || linea.EsFicticio || linea.EsBonificadoGanavisiones
            };
        }
    }

    /// <summary>
    /// NestoAPI#715: la foto del pedido en el momento de grabarlo (lo que necesita el trabajo en segundo plano, sin EF ni Request).
    /// </summary>
    public class InstantaneaAnomalias
    {
        public string Operacion { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Usuario { get; set; }
        public string Vendedor { get; set; }
        public string FormaPago { get; set; }
        public string Aplicacion { get; set; }
        public DateTime Fecha { get; set; }
        public List<LineaInstantaneaAnomalia> Lineas { get; set; } = new List<LineaInstantaneaAnomalia>();
        /// <summary>Las líneas grabadas antes (PUT) o null (POST).</summary>
        public List<LineaGrabadaAnomalia> LineasAntes { get; set; }
        /// <summary>Productos a los que la validación de precios denegó el descuento o la oferta.</summary>
        public HashSet<string> ProductosNoPermitidos { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>False si no se validó el pedido (Dirección, Almacén…) o no hizo falta (PUT sin cambios de líneas).</summary>
        public bool ValidacionHecha { get; set; }

        /// <summary>Bruto (cantidad × precio) de las líneas de producto. Es lo que se compara con el JSON de Modificaciones al etiquetar.</summary>
        public decimal ImporteBruto => Lineas.Where(l => !l.Excluida).Sum(l => l.Cantidad * l.Precio);
        public int NumeroLineas => Lineas.Count(l => !l.Excluida);

        public static InstantaneaAnomalias Desde(string operacion, PedidoVentaDTO pedido, IEnumerable<LineaGrabadaAnomalia> lineasAntes,
            RespuestaValidacion validacion, string aplicacion, DateTime ahora)
        {
            var instantanea = new InstantaneaAnomalias
            {
                Operacion = operacion,
                Empresa = string.IsNullOrWhiteSpace(pedido.empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : pedido.empresa.Trim(),
                Pedido = pedido.numero,
                Cliente = pedido.cliente?.Trim(),
                Contacto = pedido.contacto?.Trim(),
                Usuario = pedido.Usuario?.Trim(),
                Vendedor = pedido.vendedor?.Trim(),
                FormaPago = pedido.formaPago?.Trim(),
                Aplicacion = aplicacion,
                Fecha = ahora,
                LineasAntes = lineasAntes?.Where(l => l != null).ToList(),
                ValidacionHecha = validacion != null
            };
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
            {
                LineaInstantaneaAnomalia reducida = LineaInstantaneaAnomalia.Desde(linea);
                if (reducida != null)
                {
                    instantanea.Lineas.Add(reducida);
                }
            }
            if (validacion != null)
            {
                foreach (string producto in (validacion.Errores ?? new List<ErrorValidacion>()).Select(e => e?.ProductoId).Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    _ = instantanea.ProductosNoPermitidos.Add(producto.Trim());
                }
                if (!validacion.ValidacionSuperada && !string.IsNullOrWhiteSpace(validacion.ProductoId))
                {
                    _ = instantanea.ProductosNoPermitidos.Add(validacion.ProductoId.Trim());
                }
            }
            return instantanea;
        }
    }

    /// <summary>NestoAPI#715: una fila de PedidosAnomalias.</summary>
    public class FilaAnomaliaPedido
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public int? Linea { get; set; }
        public string Producto { get; set; }
        public string Regla { get; set; }
        public decimal? Valor { get; set; }
        public decimal? Esperado { get; set; }
        public decimal? Puntuacion { get; set; }
        public int? CantidadGrabada { get; set; }
        public decimal? PrecioGrabado { get; set; }
        public decimal? DescuentoGrabado { get; set; }
        public decimal? ImportePedido { get; set; }
        public int? NumeroLineas { get; set; }
        public string Usuario { get; set; }
        public string Aplicacion { get; set; }
        public string Vendedor { get; set; }
        public string Operacion { get; set; }
        public bool Mostrado { get; set; }
        public string Respuesta { get; set; }
        public string VersionModelo { get; set; }
        public decimal? DeudaTotal { get; set; }
        public decimal? DeudaVencida { get; set; }
        public int? DiasVencidoMasAntiguo { get; set; }
        public string FormaPagoFicha { get; set; }
        public string FormaPagoPedido { get; set; }
        public int? StockAlGrabar { get; set; }
        public string Resultado { get; set; }
        public DateTime? ResueltoEl { get; set; }

        public bool EsDelPedido => Linea == null;
    }

    /// <summary>
    /// NestoAPI#715, corte 4 (NO activo en el corte 1): en modo Activo, mismo patrón que <see cref="AvisoFechaEntregaLejana"/>
    /// (#712): la API rechaza el POST con 400 y código ANOMALIA_PEDIDO, el cliente pregunta «¿Seguro?» y repite con
    /// <c>ConfirmarAnomalias = true</c>; en el PUT va como <see cref="AvisoPedidoDTO"/>. Aquí solo quedan el código, el flag del
    /// DTO y la excepción, para que los clientes puedan prepararse (issues gemelas) sin que nada los lance todavía.
    /// </summary>
    public class AnomaliaPedidoException : NestoBusinessException
    {
        public const string CODIGO = "ANOMALIA_PEDIDO";

        public AnomaliaPedidoException(string mensaje, string empresa, int pedido, IEnumerable<SenalAnomalia> senales)
            : base(mensaje, new ErrorContext { ErrorCode = CODIGO, Empresa = empresa?.Trim(), Pedido = pedido })
        {
            Context.AdditionalData["senales"] = (senales ?? Enumerable.Empty<SenalAnomalia>())
                .Select(s => new { s.Regla, s.Linea, s.Producto, s.Valor, s.Esperado, s.Puntuacion }).ToList();
        }
    }
}
