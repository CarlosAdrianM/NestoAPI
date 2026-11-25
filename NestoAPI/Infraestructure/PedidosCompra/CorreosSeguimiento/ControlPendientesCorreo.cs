using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// NestoAPI#897 (regla de Carlos 25/11/26, caso Maystar): en un proveedor con control de pendientes (Proveedores.ControlPendientes) lo que
    /// no tienen se queda en una línea en el aire y lo mandan solos con un pedido siguiente, así que conviven el pedido «de pendientes» y el
    /// pedido que de verdad se espera. Con varios candidatos y sin nuestro número, el PDF ni las líneas del aviso:
    /// <list type="number">
    /// <item>«De pendientes» = ya tiene alguna línea recibida (lo que queda es resto) o se envió más de <c>dias</c> antes del correo
    /// (ParametrosUsuario «(defecto)» <see cref="CLAVE_DIAS"/>, <see cref="DIAS_POR_DEFECTO"/>). Nunca recibe la fecha por esta regla.</item>
    /// <item>De los demás («en curso»), enviados ANTES del correo: uno → ese; varios → el MÁS ANTIGUO (el otro puede venir agrupado: no se
    /// toca). Ninguno → como antes (por asignar).</item>
    /// </list>
    /// PURA.
    /// </summary>
    public static class ReglaControlPendientes
    {
        public const string CLAVE_DIAS = "Compras.DiasPedidoDePendientes";
        public const int DIAS_POR_DEFECTO = 30;

        /// <summary>Los motivos que mandan sobre esta regla (nuestro número, el PDF, las líneas del aviso, la confirmación, lo ya aplicado).</summary>
        private static readonly HashSet<string> MotivosQueMandan = new HashSet<string>
        {
            AsignadorPedidosCompra.MOTIVO_NUMERO, AsignadorPedidosCompra.MOTIVO_PDF_ADJUNTO, AsignadorPedidosCompra.MOTIVO_LINEAS_AVISO,
            AsignadorPedidosCompra.MOTIVO_PEDIDO_PROVEEDOR, AsignadorPedidosCompra.MOTIVO_MISMO_ENVIO, AsignadorPedidosCompra.MOTIVO_CONTROL_PENDIENTES
        };

        private static readonly CultureInfo es = new CultureInfo("es-ES");

        /// <summary>El parámetro: un entero de 1 a 365; si no, <see cref="DIAS_POR_DEFECTO"/>.</summary>
        public static int Interpretar(string valor) =>
            int.TryParse(valor?.Trim(), out int dias) && dias >= 1 && dias <= 365 ? dias : DIAS_POR_DEFECTO;

        /// <summary>Lee el parámetro (sin crear filas); si no se puede, el de por defecto.</summary>
        public static int LeerDias(Action<Exception> registrar = null)
        {
            try
            {
                return Interpretar(NestoAPI.Controllers.ParametrosUsuarioController.LeerParametroSinDefecto(
                    NestoAPI.Models.Constantes.Empresas.EMPRESA_POR_DEFECTO, ServicioAplicacionCorreosCompras.USUARIO_DEFECTO, CLAVE_DIAS));
            }
            catch (Exception ex)
            {
                registrar?.Invoke(new Exception($"[Correos de seguimiento de compras #897] No se ha podido leer {CLAVE_DIAS}: se usan {DIAS_POR_DEFECTO} días. {ex.Message}", ex));
                return DIAS_POR_DEFECTO;
            }
        }

        /// <summary>Cuándo se envió: el primer envío (PedidosCompraEnvios) o, si no lo hay, CabPedidoCmp.[Fecha Modificación].</summary>
        public static DateTime? FechaEnvio(PedidoCompraEnAire pedido) => pedido?.FechaPrimerEnvio ?? pedido?.FechaEnvio;

        /// <summary>Por qué un pedido es «de pendientes» respecto a un correo recibido ese día; null si está en curso.</summary>
        public static string MotivoDePendientes(PedidoCompraEnAire pedido, DateTime recibido, int dias)
        {
            if (pedido == null)
            {
                return null;
            }
            if (pedido.LineasYaRecibidas > 0)
            {
                return pedido.LineasYaRecibidas == 1
                    ? "ya tiene 1 línea recibida: lo que queda es resto"
                    : $"ya tiene {pedido.LineasYaRecibidas} líneas recibidas: lo que queda es resto";
            }
            DateTime? enviado = FechaEnvio(pedido);
            if (enviado.HasValue && enviado.Value < recibido.AddDays(-Math.Max(1, dias)))
            {
                return $"se envió el {enviado.Value.ToString("dd/MM/yyyy", es)}, hace más de {Math.Max(1, dias)} días";
            }
            return null;
        }

        /// <summary>Lo que dice la regla de un envío (para el texto y los tests).</summary>
        public class Eleccion
        {
            public PedidoCompraEnAire Elegido { get; set; }
            public List<PedidoCompraEnAire> DePendientes { get; set; } = new List<PedidoCompraEnAire>();
            /// <summary>Los en curso que no se eligen (pueden venir en el mismo envío).</summary>
            public List<PedidoCompraEnAire> OtrosEnCurso { get; set; } = new List<PedidoCompraEnAire>();
            public string Explicacion { get; set; }
        }

        /// <summary>La elección entre los candidatos de UN proveedor con control de pendientes; null si la regla no decide.</summary>
        public static Eleccion Elegir(IEnumerable<PedidoCompraEnAire> candidatos, DateTime recibido, int dias)
        {
            List<PedidoCompraEnAire> lista = (candidatos ?? Enumerable.Empty<PedidoCompraEnAire>()).Where(p => p != null)
                .GroupBy(p => AsignadorPedidosCompra.Clave(p.Empresa, p.Pedido)).Select(g => g.First()).ToList();
            if (lista.Count < 2 || !lista.All(p => p.ControlPendientes)
                || lista.Select(p => p.Proveedor?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            {
                return null;
            }
            var eleccion = new Eleccion
            {
                DePendientes = lista.Where(p => MotivoDePendientes(p, recibido, dias) != null).OrderBy(p => p.Pedido).ToList()
            };
            List<PedidoCompraEnAire> enCurso = lista.Where(p => !eleccion.DePendientes.Contains(p)
                    && FechaEnvio(p).HasValue && FechaEnvio(p).Value < recibido)
                .OrderBy(p => FechaEnvio(p).Value).ThenBy(p => p.Pedido).ToList();
            if (!enCurso.Any())
            {
                return null;
            }
            eleccion.Elegido = enCurso[0];
            eleccion.OtrosEnCurso = lista.Where(p => p != eleccion.Elegido && !eleccion.DePendientes.Contains(p)).OrderBy(p => p.Pedido).ToList();
            eleccion.Explicacion = Texto(eleccion);
            return eleccion;
        }

        /// <summary>
        /// «MAYSTAR tiene control de pendientes: se asigna al pedido en curso más antiguo, 220983; el 220093 es de pendientes y el 220993
        /// puede venir en el mismo envío.»
        /// </summary>
        internal static string Texto(Eleccion e)
        {
            string nombre = string.IsNullOrWhiteSpace(e.Elegido.NombreProveedor) ? "El proveedor " + e.Elegido.Proveedor?.Trim() : e.Elegido.NombreProveedor.Trim();
            bool varios = e.OtrosEnCurso.Any();
            string texto = $"{nombre} tiene control de pendientes: se asigna al pedido en curso {(varios ? "más antiguo, " : string.Empty)}{e.Elegido.Pedido}";
            var partes = new List<string>();
            if (e.DePendientes.Any())
            {
                partes.Add(e.DePendientes.Count == 1
                    ? $"el {e.DePendientes[0].Pedido} es de pendientes"
                    : $"los {Lista(e.DePendientes)} son de pendientes");
            }
            if (varios)
            {
                partes.Add(e.OtrosEnCurso.Count == 1
                    ? $"el {e.OtrosEnCurso[0].Pedido} puede venir en el mismo envío"
                    : $"los {Lista(e.OtrosEnCurso)} pueden venir en el mismo envío");
            }
            return texto + (partes.Any() ? "; " + string.Join(" y ", partes) : string.Empty) + ".";
        }

        private static string Lista(List<PedidoCompraEnAire> pedidos)
        {
            List<string> numeros = pedidos.Select(p => p.Pedido.ToString(CultureInfo.InvariantCulture)).ToList();
            return numeros.Count == 1 ? numeros[0] : string.Join(", ", numeros.Take(numeros.Count - 1)) + " y " + numeros.Last();
        }

        /// <summary>
        /// La asignación con la regla aplicada (un solo candidato, motivo <see cref="AsignadorPedidosCompra.MOTIVO_CONTROL_PENDIENTES"/> y su
        /// explicación) o la misma si no toca: ya manda otro criterio, un solo candidato, varios proveedores, sin control de pendientes o
        /// ninguno en curso.
        /// </summary>
        public static AsignacionPedidoCompra Completar(AsignacionPedidoCompra asignacion, IEnumerable<PedidoCompraEnAire> pedidos, DateTime recibido, int dias) =>
            Completar(asignacion, pedidos, recibido, () => dias);

        /// <param name="dias">Se pide solo si la regla tiene algo que decidir (el parámetro no se lee para cualquier correo).</param>
        public static AsignacionPedidoCompra Completar(AsignacionPedidoCompra asignacion, IEnumerable<PedidoCompraEnAire> pedidos, DateTime recibido, Func<int> dias)
        {
            if (asignacion?.Candidatos == null || asignacion.Candidatos.Count < 2
                || asignacion.Candidatos.Any(c => MotivosQueMandan.Contains(c.Motivo ?? string.Empty)))
            {
                return asignacion;
            }
            List<PedidoCompraEnAire> delEnvio = ReglaPedidoSugerido.DelEnvio(pedidos, asignacion.Candidatos);
            if (delEnvio.Count != asignacion.Candidatos.Count)
            {
                return asignacion; // algún candidato sin sus datos: no se decide a medias
            }
            if (!delEnvio.All(p => p.ControlPendientes))
            {
                return asignacion;
            }
            Eleccion eleccion = Elegir(delEnvio, recibido, dias());
            if (eleccion == null)
            {
                return asignacion;
            }
            return new AsignacionPedidoCompra
            {
                ProveedorPropuesto = eleccion.Elegido.Proveedor?.Trim(),
                Candidatos = new List<CandidatoPedidoCompra>
                {
                    new CandidatoPedidoCompra
                    {
                        Empresa = eleccion.Elegido.Empresa?.Trim(), Pedido = eleccion.Elegido.Pedido, Proveedor = eleccion.Elegido.Proveedor?.Trim(),
                        Motivo = AsignadorPedidosCompra.MOTIVO_CONTROL_PENDIENTES, Explicacion = eleccion.Explicacion
                    }
                }
            };
        }

        /// <summary>La explicación guardada en los candidatos de un correo (null si no la hay).</summary>
        /// <param name="pedido">Si se da, solo la del candidato de ese pedido.</param>
        public static string ExplicacionDe(CorreoSeguimientoFila fila, CandidatoPedidoCompra pedido = null) =>
            AsignadorPedidosCompra.Deserializar(fila?.PedidosCandidatos)
                .Where(c => pedido == null || AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido) == AsignadorPedidosCompra.Clave(pedido.Empresa, pedido.Pedido))
                .Select(c => c.Explicacion).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
    }

    /// <summary>NestoAPI#897: bultos y kilos que dice el correo de una agencia (solo para enseñarlos; nunca deciden).</summary>
    public class BultosKilosEnvio
    {
        public int? Bultos { get; set; }
        public decimal? Kilos { get; set; }
    }

    /// <summary>NestoAPI#897: la agencia sabe leer en sus correos los bultos y los kilos del envío (lo implementa SOLO su clase).</summary>
    public interface IBultosKilosAgencia
    {
        BultosKilosEnvio LeerBultosKilos(string texto);
    }

    /// <summary>NestoAPI#897: los bultos y kilos de un correo guardado, de su agencia (por el nombre) y del texto que se tiene (el extracto).</summary>
    public static class BultosKilosCorreo
    {
        public static BultosKilosEnvio De(string agencia, string texto)
        {
            if (string.IsNullOrWhiteSpace(agencia) || string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            IBultosKilosAgencia lectora = AgenciasEntradaRegistradas.Todas
                .FirstOrDefault(a => string.Equals(a.Nombre, agencia.Trim(), StringComparison.OrdinalIgnoreCase)) as IBultosKilosAgencia;
            BultosKilosEnvio leido = lectora?.LeerBultosKilos(texto);
            return leido != null && (leido.Bultos.HasValue || leido.Kilos.HasValue) ? leido : null;
        }

        /// <summary>«8 bultos, 123 kg» (null si no hay nada).</summary>
        public static string Texto(BultosKilosEnvio b)
        {
            if (b == null)
            {
                return null;
            }
            var partes = new List<string>();
            if (b.Bultos.HasValue)
            {
                partes.Add(b.Bultos.Value == 1 ? "1 bulto" : $"{b.Bultos.Value} bultos");
            }
            if (b.Kilos.HasValue)
            {
                partes.Add($"{b.Kilos.Value.ToString("0.###", new CultureInfo("es-ES"))} kg");
            }
            return partes.Any() ? string.Join(", ", partes) : null;
        }

        private static readonly Regex Numero = new Regex(@"^\d{1,6}(?:[.,]\d{1,3})?$", RegexOptions.Compiled);

        internal static decimal? Decimal(string valor)
        {
            string limpio = valor?.Trim();
            if (string.IsNullOrEmpty(limpio) || !Numero.IsMatch(limpio))
            {
                return null;
            }
            return decimal.TryParse(limpio.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) ? d : (decimal?)null;
        }
    }
}
