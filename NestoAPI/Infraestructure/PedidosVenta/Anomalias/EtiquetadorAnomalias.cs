using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta.Anomalias
{
    /// <summary>NestoAPI#715: una fila de Modificaciones (Tabla = 'Pedidos') ya parseada: el pedido tal y como lo mandó el cliente en ese PUT.</summary>
    public class ModificacionPedido
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        public int Pedido { get; set; }
        /// <summary>Las líneas de producto del JSON (sin cheques, cupones, ficticias ni bonificados).</summary>
        public List<LineaModificacion> Lineas { get; } = new List<LineaModificacion>();
        /// <summary>False si el JSON no se pudo leer (entonces solo cuenta como «hubo una modificación»).</summary>
        public bool Parseada { get; set; }

        public decimal ImporteBruto => Lineas.Sum(l => l.Cantidad * l.Precio);
        public int NumeroLineas => Lineas.Count;

        /// <summary>Lee el JSON de PedidoVentaDTO que guarda PedidosVentaController en Modificaciones.Nuevo. Nunca lanza; null si no es de un pedido.</summary>
        public static ModificacionPedido Parsear(ModificacionCruda cruda)
        {
            if (cruda == null)
            {
                return null;
            }
            var modificacion = new ModificacionPedido { Id = cruda.Id, Fecha = cruda.Fecha, Usuario = cruda.Usuario };
            try
            {
                JObject json = JObject.Parse(cruda.Nuevo ?? string.Empty);
                int? numero = Entero(json["numero"]);
                if (!numero.HasValue)
                {
                    return null;
                }
                modificacion.Pedido = numero.Value;
                foreach (JToken token in json["Lineas"] as JArray ?? new JArray())
                {
                    if (!(token is JObject linea))
                    {
                        continue;
                    }
                    int tipo = Entero(linea["tipoLinea"]) ?? 1;
                    string producto = linea["Producto"]?.Type == JTokenType.String ? ((string)linea["Producto"])?.Trim() : null;
                    if (tipo != 1 || string.IsNullOrEmpty(producto) || Verdadero(linea["EsChequeRegalo"]) || Verdadero(linea["EsCupon"])
                        || Verdadero(linea["EsFicticio"]) || Verdadero(linea["EsBonificadoGanavisiones"]))
                    {
                        continue;
                    }
                    bool aplicar = !linea.ContainsKey("AplicarDescuento") || Verdadero(linea["AplicarDescuento"]);
                    decimal entidad = Numero(linea["DescuentoEntidad"]) ?? 0;
                    decimal productoDto = Numero(linea["DescuentoProducto"]) ?? 0;
                    decimal lineaDto = Numero(linea["DescuentoLinea"]) ?? 0;
                    modificacion.Lineas.Add(new LineaModificacion
                    {
                        Id = Entero(linea["id"]) ?? 0,
                        Producto = producto,
                        Cantidad = Entero(linea["Cantidad"]) ?? 0,
                        Precio = Numero(linea["PrecioUnitario"]) ?? 0,
                        Descuento = aplicar ? 1 - (1 - entidad) * (1 - productoDto) * (1 - lineaDto) : lineaDto
                    });
                }
                modificacion.Parseada = true;
            }
            catch
            {
                // JSON raro (otra versión del DTO, truncado…): cuenta solo como «hubo una modificación» si sabemos el pedido
                if (modificacion.Pedido == 0)
                {
                    return null;
                }
            }
            return modificacion;
        }

        private static int? Entero(JToken token)
        {
            decimal? numero = Numero(token);
            return numero.HasValue ? (int?)decimal.ToInt32(decimal.Truncate(numero.Value)) : null;
        }

        /// <summary>Con el texto JSON (invariante): JToken.ToString() usa la cultura del hilo y en es-ES 0.22 sale como «0,22».</summary>
        private static decimal? Numero(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            string texto = token.Type == JTokenType.String ? (string)token : token.ToString(Newtonsoft.Json.Formatting.None);
            return decimal.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal valor) ? valor : (decimal?)null;
        }

        private static bool Verdadero(JToken token) => token != null && token.Type == JTokenType.Boolean && (bool)token;
    }

    public class LineaModificacion
    {
        public int Id { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public decimal Precio { get; set; }
        /// <summary>Descuento sin pronto pago (fracción).</summary>
        public decimal Descuento { get; set; }
    }

    /// <summary>
    /// NestoAPI#715: la etiqueta automática de cada fila de PedidosAnomalias. Pura.
    /// <list type="bullet">
    /// <item><c>CorregidoEn48h</c>: la línea se cambió (cantidad, precio o descuento) o se borró dentro de las 48 h siguientes a
    /// grabarla, según LinPedidoVta ([Fecha Modificación] y los valores actuales frente a los grabados) y la tabla Modificaciones
    /// (el JSON de cada PUT de la API). En las filas del pedido (Contexto, ImportePedido): el número de líneas o el bruto cambian.</item>
    /// <item><c>Abonado</c>: a los 7 días, hay una línea negativa del mismo producto para el cliente después de grabar.</item>
    /// <item><c>SinCambios</c>: a los 7 días, nada de lo anterior.</item>
    /// </list>
    /// Una línea borrada después de 48 h sin rastro en Modificaciones (Nesto viejo) no se puede fechar: a los 7 días queda SinCambios,
    /// que es lo correcto por definición (no se corrigió en 48 h).
    /// </summary>
    public static class EtiquetadorAnomalias
    {
        public const int HORAS_CORRECCION = 48;
        public const int DIAS_SIN_CAMBIOS = 7;
        /// <summary>Tolerancia entre el reloj de la API (Fecha de la fila) y el de la BD (Modificaciones.Fecha, [Fecha Modificación]).</summary>
        public static readonly TimeSpan TOLERANCIA_RELOJ = TimeSpan.FromMinutes(1);
        private const decimal TOLERANCIA_IMPORTE = 0.01m;
        private const decimal TOLERANCIA_DESCUENTO = 0.000001m;

        /// <summary>
        /// Devuelve true y rellena Resultado y ResueltoEl si la fila ya se puede etiquetar.
        /// </summary>
        /// <param name="lineasActuales">Las líneas de producto que hoy tiene el pedido (vacío = sin líneas o borrado).</param>
        /// <param name="modificaciones">Las de Modificaciones de ese pedido.</param>
        /// <param name="abonos">Las líneas negativas del cliente desde que se grabó.</param>
        public static bool Etiquetar(FilaAnomaliaPedido fila, IList<EstadoLineaActual> lineasActuales, IList<ModificacionPedido> modificaciones,
            IList<AbonoCliente> abonos, DateTime ahora)
        {
            if (fila == null || fila.Resultado != null)
            {
                return false;
            }
            lineasActuales = lineasActuales ?? new List<EstadoLineaActual>();
            List<ModificacionPedido> enVentana = (modificaciones ?? new List<ModificacionPedido>())
                .Where(m => m != null && m.Pedido == fila.Pedido && EnVentana(fila, m.Fecha))
                .OrderBy(m => m.Fecha)
                .ToList();
            DateTime limite = fila.Fecha.AddHours(HORAS_CORRECCION);

            DateTime? corregido = EsDeLinea(fila)
                ? CorreccionLinea(fila, lineasActuales, enVentana, ahora, limite)
                : CorreccionPedido(fila, lineasActuales, enVentana, ahora, limite);
            if (corregido.HasValue)
            {
                fila.Resultado = ResultadosAnomalia.CORREGIDO_48H;
                fila.ResueltoEl = corregido.Value;
                return true;
            }
            if (ahora < fila.Fecha.AddDays(DIAS_SIN_CAMBIOS))
            {
                return false;
            }
            DateTime? abonado = Abono(fila, lineasActuales, abonos);
            fila.Resultado = abonado.HasValue ? ResultadosAnomalia.ABONADO : ResultadosAnomalia.SIN_CAMBIOS;
            fila.ResueltoEl = abonado ?? ahora;
            return true;
        }

        internal static bool EsDeLinea(FilaAnomaliaPedido fila) => fila.Linea.HasValue || !string.IsNullOrWhiteSpace(fila.Producto);

        private static bool EnVentana(FilaAnomaliaPedido fila, DateTime fecha)
            => fecha >= fila.Fecha - TOLERANCIA_RELOJ && fecha <= fila.Fecha.AddHours(HORAS_CORRECCION);

        private static DateTime? CorreccionLinea(FilaAnomaliaPedido fila, IList<EstadoLineaActual> lineasActuales, List<ModificacionPedido> enVentana, DateTime ahora, DateTime limite)
        {
            // 1. Un PUT de la API dentro de las 48 h en el que la línea ya no está o va distinta
            foreach (ModificacionPedido modificacion in enVentana.Where(m => m.Parseada))
            {
                LineaModificacion linea = fila.Linea.HasValue
                    ? modificacion.Lineas.FirstOrDefault(l => l.Id == fila.Linea.Value)
                    : modificacion.Lineas.FirstOrDefault(l => string.Equals(l.Producto, fila.Producto?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (linea == null || Distinta(fila, linea.Cantidad, linea.Precio, linea.Descuento))
                {
                    return modificacion.Fecha;
                }
            }
            // 2. Lo que hay hoy en LinPedidoVta (también lo que cambia Nesto viejo sin pasar por la API)
            EstadoLineaActual actual = fila.Linea.HasValue
                ? lineasActuales.FirstOrDefault(l => l.Linea == fila.Linea.Value)
                : lineasActuales.FirstOrDefault(l => string.Equals(l.Producto?.Trim(), fila.Producto?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (actual == null)
            {
                // Borrada. Si aún estamos en las 48 h, o hubo alguna modificación en ellas (aunque el JSON no se leyera), fue dentro de plazo.
                if (ahora <= limite)
                {
                    return ahora;
                }
                return enVentana.Any() ? enVentana.Last().Fecha : (DateTime?)null;
            }
            if (Distinta(fila, actual.Cantidad, actual.Precio, actual.Descuento) && actual.FechaModificacion >= fila.Fecha - TOLERANCIA_RELOJ && actual.FechaModificacion <= limite)
            {
                return actual.FechaModificacion;
            }
            return null;
        }

        private static DateTime? CorreccionPedido(FilaAnomaliaPedido fila, IList<EstadoLineaActual> lineasActuales, List<ModificacionPedido> enVentana, DateTime ahora, DateTime limite)
        {
            foreach (ModificacionPedido modificacion in enVentana.Where(m => m.Parseada))
            {
                if (PedidoDistinto(fila, modificacion.NumeroLineas, modificacion.ImporteBruto))
                {
                    return modificacion.Fecha;
                }
            }
            if (!lineasActuales.Any())
            {
                if (ahora <= limite)
                {
                    return ahora;
                }
                return enVentana.Any() ? enVentana.Last().Fecha : (DateTime?)null;
            }
            decimal bruto = lineasActuales.Sum(l => l.Cantidad * l.Precio);
            if (PedidoDistinto(fila, lineasActuales.Count, bruto))
            {
                DateTime ultima = lineasActuales.Max(l => l.FechaModificacion);
                if (ultima >= fila.Fecha - TOLERANCIA_RELOJ && ultima <= limite)
                {
                    return ultima;
                }
            }
            return null;
        }

        private static DateTime? Abono(FilaAnomaliaPedido fila, IList<EstadoLineaActual> lineasActuales, IList<AbonoCliente> abonos)
        {
            if (abonos == null || string.IsNullOrWhiteSpace(fila.Cliente))
            {
                return null;
            }
            HashSet<string> productos = EsDeLinea(fila)
                ? new HashSet<string>(new[] { fila.Producto?.Trim() ?? string.Empty }, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(lineasActuales.Select(l => l.Producto?.Trim()).Where(p => !string.IsNullOrEmpty(p)), StringComparer.OrdinalIgnoreCase);
            AbonoCliente abono = abonos
                .Where(a => a != null && string.Equals(a.Cliente?.Trim(), fila.Cliente.Trim(), StringComparison.OrdinalIgnoreCase)
                    && a.Fecha > fila.Fecha && a.Pedido != fila.Pedido && productos.Contains(a.Producto?.Trim() ?? string.Empty))
                .OrderBy(a => a.Fecha)
                .FirstOrDefault();
            return abono?.Fecha;
        }

        private static bool Distinta(FilaAnomaliaPedido fila, int cantidad, decimal precio, decimal descuento)
        {
            if (fila.CantidadGrabada.HasValue && fila.CantidadGrabada.Value != cantidad)
            {
                return true;
            }
            if (fila.PrecioGrabado.HasValue && Math.Abs(fila.PrecioGrabado.Value - precio) > TOLERANCIA_IMPORTE)
            {
                return true;
            }
            return fila.DescuentoGrabado.HasValue && Math.Abs(fila.DescuentoGrabado.Value - descuento) > TOLERANCIA_DESCUENTO;
        }

        private static bool PedidoDistinto(FilaAnomaliaPedido fila, int lineas, decimal bruto)
        {
            if (fila.NumeroLineas.HasValue && fila.NumeroLineas.Value != lineas)
            {
                return true;
            }
            return fila.ImportePedido.HasValue && Math.Abs(fila.ImportePedido.Value - bruto) > TOLERANCIA_IMPORTE;
        }

        /// <summary>
        /// Etiqueta todas las pendientes con lo que hay en la BD. Devuelve las que han cambiado (ya con Resultado y ResueltoEl puestos).
        /// </summary>
        public static List<FilaAnomaliaPedido> EtiquetarPendientes(IRepositorioAnomaliasPedido repositorio, DateTime ahora)
        {
            var etiquetadas = new List<FilaAnomaliaPedido>();
            List<FilaAnomaliaPedido> pendientes = repositorio.LeerPendientesEtiquetar(ahora) ?? new List<FilaAnomaliaPedido>();
            if (!pendientes.Any())
            {
                return etiquetadas;
            }
            foreach (IGrouping<string, FilaAnomaliaPedido> porEmpresa in pendientes.GroupBy(f => f.Empresa?.Trim() ?? string.Empty))
            {
                List<int> pedidos = porEmpresa.Select(f => f.Pedido).Distinct().ToList();
                DateTime desde = porEmpresa.Min(f => f.Fecha) - TOLERANCIA_RELOJ;
                ILookup<int, EstadoLineaActual> lineas = (repositorio.LeerLineasActuales(porEmpresa.Key, pedidos) ?? new List<EstadoLineaActual>())
                    .ToLookup(l => l.Pedido);
                ILookup<int, ModificacionPedido> modificaciones = (repositorio.LeerModificaciones(desde, ahora, pedidos) ?? new List<ModificacionCruda>())
                    .Select(ModificacionPedido.Parsear)
                    .Where(m => m != null)
                    .ToLookup(m => m.Pedido);
                List<string> clientes = porEmpresa.Select(f => f.Cliente?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                List<AbonoCliente> abonos = repositorio.LeerAbonos(porEmpresa.Key, clientes, desde) ?? new List<AbonoCliente>();
                foreach (FilaAnomaliaPedido fila in porEmpresa)
                {
                    if (Etiquetar(fila, lineas[fila.Pedido].ToList(), modificaciones[fila.Pedido].ToList(), abonos, ahora))
                    {
                        etiquetadas.Add(fila);
                    }
                }
            }
            if (etiquetadas.Any())
            {
                repositorio.Etiquetar(etiquetadas);
            }
            return etiquetadas;
        }
    }
}
