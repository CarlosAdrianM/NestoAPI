using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>NestoAPI#631: lo que puede decidir Compras (o Dirección) sobre una propuesta. Se guarda tal cual en PropuestasStock.Decision.</summary>
    public static class DecisionesPropuesta
    {
        public const string APLICAR = "Aplicar";
        public const string RECHAZAR = "Rechazar";
        public const string POSPONER = "Posponer";
        public const string SILENCIAR = "Silenciar";
        /// <summary>
        /// NestoAPI#655: en vez de liquidar, darle un plazo para vender más: objetivo de venta (uds/mes) y fecha de revisión.
        /// Necesita Scripts/Issue655_PotenciarVentas.sql.
        /// </summary>
        public const string POTENCIAR = "Potenciar";

        public static readonly string[] TODAS = { APLICAR, RECHAZAR, POSPONER, SILENCIAR, POTENCIAR };

        /// <summary>«aplicar» → «Aplicar»; null si no es ninguna.</summary>
        public static string Normalizar(string decision)
            => decision == null ? null : Array.Find(TODAS, d => string.Equals(d, decision.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// NestoAPI#631: estado de una fila de PropuestasStock. No se guarda: lo calcula la consulta
    /// (<see cref="RepositorioDecisionesPropuestasStockSql.SQL_ESTADO"/>) con la decisión, la acción, si hay una propuesta más
    /// reciente del producto y si hay un silencio vigente.
    /// </summary>
    public static class EstadosPropuesta
    {
        /// <summary>Hay que decidir.</summary>
        public const string PENDIENTE = "Pendiente";
        public const string APLICADA = "Aplicada";
        public const string RECHAZADA = "Rechazada";
        /// <summary>Se decidirá más adelante; vuelve a salir (como fila nueva) el mes que viene.</summary>
        public const string POSPUESTA = "Pospuesta";
        /// <summary>Silenciada en esta fila o heredada de un silencio anterior que aún no ha caducado.</summary>
        public const string SILENCIADA = "Silenciada";
        /// <summary>Sin decidir y ya hay una propuesta más reciente del mismo producto: se decide sobre la última.</summary>
        public const string SUPERADA = "Superada";
        /// <summary>«Mantener» o «Nada»: no hay nada que decidir.</summary>
        public const string SIN_ACCION = "SinAccion";
        /// <summary>
        /// NestoAPI#655: con una potenciación vigente (de esta fila o de una anterior del producto) que no ha llegado a su fecha de
        /// revisión: no es exceso pendiente. En la pantalla, «En potenciación» con la venta media frente al objetivo
        /// (<see cref="PropuestaStockConDecision.NotaPotenciacion"/>). Pasada la fecha, la fila vuelve a su estado normal.
        /// </summary>
        public const string EN_POTENCIACION = "EnPotenciacion";

        public static readonly string[] TODOS = { PENDIENTE, APLICADA, RECHAZADA, POSPUESTA, SILENCIADA, SUPERADA, SIN_ACCION, EN_POTENCIACION };

        public static string Normalizar(string estado)
            => estado == null ? null : Array.Find(TODOS, e => string.Equals(e, estado.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// NestoAPI#631: por qué se silencia. Decisión de Carlos (15/10/26): aún no se sabe qué motivos saldrán, así que la lista
    /// se amplía sin publicar con el parámetro <see cref="CLAVE"/> de la fila «(defecto)» de la empresa por defecto
    /// (Scripts/Issue631_MotivosSilencio.sql): separada por comas o punto y coma. Sin la fila, o vacía, los tres de la issue
    /// (<see cref="POR_DEFECTO"/>). La columna PropuestasStock.MotivoSilencio es varchar(20) sin CHECK de valores: un motivo
    /// más largo de <see cref="LONGITUD_MAXIMA"/> caracteres no se ofrece.
    /// </summary>
    public static class MotivosSilencio
    {
        public const string NUEVO = "Nuevo";
        public const string ESTACIONAL = "Estacional";
        public const string APARATO = "Aparato";

        public const string CLAVE = "MotivosSilencioPropuestas";
        public const string POR_DEFECTO = NUEVO + ", " + ESTACIONAL + ", " + APARATO;
        /// <summary>PropuestasStock.MotivoSilencio y PropuestasStockDecisiones.MotivoSilencio son varchar(20).</summary>
        public const int LONGITUD_MAXIMA = 20;

        /// <summary>Los tres de la issue (la lista cuando no hay parámetro).</summary>
        public static readonly string[] TODOS = { NUEVO, ESTACIONAL, APARATO };

        /// <summary>La lista que vale con el valor del parámetro (null o vacío: <see cref="POR_DEFECTO"/>), sin repetidos ni demasiado largos.</summary>
        public static List<string> Lista(string valor)
        {
            List<string> lista = new List<string>();
            foreach (string motivo in (valor ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string limpio = motivo.Trim();
                if (limpio.Length > 0 && limpio.Length <= LONGITUD_MAXIMA && !lista.Exists(m => string.Equals(m, limpio, StringComparison.OrdinalIgnoreCase)))
                {
                    lista.Add(limpio);
                }
            }
            return lista.Count > 0 ? lista : new List<string>(TODOS);
        }

        /// <summary>«estacional» → «Estacional» (como esté escrito en la lista); null si no está en <paramref name="lista"/>.</summary>
        public static string Normalizar(string motivo, IEnumerable<string> lista)
        {
            if (motivo == null || lista == null)
            {
                return null;
            }
            foreach (string m in lista)
            {
                if (string.Equals(m, motivo.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return m;
                }
            }
            return null;
        }

        /// <summary>Con la lista por defecto.</summary>
        public static string Normalizar(string motivo) => Normalizar(motivo, TODOS);
    }

    /// <summary>NestoAPI#631: a dónde se propone liquidar (de momento solo se apunta: la acción es manual).</summary>
    public static class CanalesLiquidacion
    {
        public const string OUTLET = "Outlet";
        public const string GANAVISIONES = "Ganavisiones";

        public static readonly string[] TODOS = { OUTLET, GANAVISIONES };

        public static string Normalizar(string canal)
            => canal == null ? null : Array.Find(TODOS, c => string.Equals(c, canal.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Cuerpo de POST api/PropuestasStock/{id}/Decision.</summary>
    public class DecisionPropuestaStockDTO
    {
        /// <summary>Aplicar, Rechazar, Posponer o Silenciar.</summary>
        public string Decision { get; set; }
        /// <summary>Texto libre. Obligatorio al rechazar y al aplicar con un máximo o mínimo distinto del propuesto.</summary>
        public string Motivo { get; set; }
        /// <summary>Solo al aplicar un «Ajustar máx/mín»: el máximo que se pone si no es el propuesto.</summary>
        public int? StockMaximo { get; set; }
        /// <summary>Solo al aplicar un «Ajustar máx/mín»: el mínimo que se pone si no es el propuesto.</summary>
        public int? StockMinimo { get; set; }
        /// <summary>Solo al silenciar: uno de GET api/PropuestasStock/MotivosSilencio (por defecto, Nuevo, Estacional o Aparato).</summary>
        public string MotivoSilencio { get; set; }
        /// <summary>Solo al silenciar: hasta cuándo (incluido) no vuelve a salir como pendiente.</summary>
        public DateTime? SilenciarHasta { get; set; }
        /// <summary>Solo al aplicar un «Liquidar»: Outlet o Ganavisiones (opcional; se apunta: el máximo y el mínimo del almacén
        /// pasan a 0, pero el paso al Outlet o a Ganavisiones lo hace la persona a mano).</summary>
        public string CanalLiquidacion { get; set; }
        /// <summary>NestoAPI#655: solo al potenciar (obligatorio): la venta que se quiere conseguir, en unidades al mes.</summary>
        public int? ObjetivoVentaMensual { get; set; }
        /// <summary>NestoAPI#655: solo al potenciar: la fecha de revisión (incluida). Sin ella, dentro de 6 meses.</summary>
        public DateTime? PotenciarHasta { get; set; }
    }

    /// <summary>Respuesta de POST api/PropuestasStock/{id}/Decision.</summary>
    public class ResultadoDecisionPropuestaStock
    {
        public int Id { get; set; }
        public string Producto { get; set; }
        public string Decision { get; set; }
        /// <summary>El estado en que queda la propuesta (<see cref="EstadosPropuesta"/>).</summary>
        public string Estado { get; set; }
        /// <summary>Lo que se ha escrito, legible: «ALG: máximo 555 → 364, mínimo 295 → 220», «Estado 0 → 1».</summary>
        public List<string> Cambios { get; set; } = new List<string>();
        public string Mensaje { get; set; }
    }

    /// <summary>Una fila de GET api/PropuestasStock/Resumen.</summary>
    public class ResumenEstadoPropuestas
    {
        public DateTime Mes { get; set; }
        public string Estado { get; set; }
        public int Propuestas { get; set; }
        public decimal ExcesoValorado { get; set; }
    }

    /// <summary>Una fila de PropuestasStock con lo del corte 3 (decisión, valores aplicados, silencio y estado calculado).</summary>
    public class PropuestaStockConDecision : PropuestaStockGuardada
    {
        /// <summary>Calculado (<see cref="EstadosPropuesta"/>).</summary>
        public string Estado { get; set; }
        public string MotivoSilencio { get; set; }
        public DateTime? SilenciadaHasta { get; set; }
        public string CanalLiquidacion { get; set; }
        public int? StockMaximoAnterior { get; set; }
        public int? StockMinimoAnterior { get; set; }
        public int? StockMaximoAplicado { get; set; }
        public int? StockMinimoAplicado { get; set; }
        public short? EstadoAnterior { get; set; }
        public short? EstadoAplicado { get; set; }
        /// <summary>Si la silencia otra fila (o esta misma): su Id, hasta cuándo y por qué.</summary>
        public int? SilencioPropuestaId { get; set; }
        public DateTime? SilencioHasta { get; set; }
        public string SilencioMotivo { get; set; }
        /// <summary>El mes de la propuesta más reciente del mismo producto y almacén, si la hay (entonces esta ya no se puede decidir).</summary>
        public DateTime? MesMasReciente { get; set; }

        // NestoAPI#655: potenciar ventas. Sin Scripts/Issue655_PotenciarVentas.sql la consulta los devuelve a NULL.
        /// <summary>Lo decidido en esta fila (solo si su decisión es «Potenciar»).</summary>
        public int? ObjetivoVentaMensual { get; set; }
        public DateTime? PotenciarHasta { get; set; }
        /// <summary>La última potenciación del producto y almacén (de esta fila o de una anterior): su Id, objetivo y fecha de revisión.</summary>
        public int? PotenciacionPropuestaId { get; set; }
        public int? PotenciacionObjetivo { get; set; }
        public DateTime? PotenciacionHasta { get; set; }
        /// <summary>La de <see cref="PotenciacionPropuestaId"/> no ha llegado a su fecha de revisión.</summary>
        public bool PotenciacionVigente { get; set; }

        /// <summary>
        /// NestoAPI#655: cómo va la potenciación, con la venta media de 6 meses de esta fila como «real». Vigente: «En potenciación
        /// hasta el …: venta media X uds/mes de un objetivo de Y (Z %)». Pasada la fecha: «potenciación cumplida» o «potenciación sin
        /// cumplir (objetivo X, real Y)». Null si el producto no tiene ninguna. Solo lectura (no es columna).
        /// </summary>
        public string NotaPotenciacion
        {
            get
            {
                if (!PotenciacionPropuestaId.HasValue || !PotenciacionObjetivo.HasValue)
                {
                    return null;
                }
                CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
                int objetivo = PotenciacionObjetivo.Value;
                string real = VentaMedia6Meses.ToString("N1", es);
                if (PotenciacionVigente)
                {
                    string porcentaje = objetivo > 0 ? $" ({Math.Round(VentaMedia6Meses * 100m / objetivo, 0).ToString("N0", es)} %)" : string.Empty;
                    return $"En potenciación hasta el {PotenciacionHasta:dd/MM/yyyy}: venta media {real} uds/mes de un objetivo de {objetivo}{porcentaje}";
                }
                return VentaMedia6Meses >= objetivo
                    ? $"potenciación cumplida (objetivo {objetivo}, real {real})"
                    : $"potenciación sin cumplir (objetivo {objetivo}, real {real})";
            }
        }
    }

    /// <summary>El control de stock de un almacén tal como está ahora.</summary>
    public class ControlStockActual
    {
        public string Almacen { get; set; }
        public int StockMaximo { get; set; }
        public int StockMinimo { get; set; }
    }

    /// <summary>Un cambio de máximo y mínimo en un almacén. <see cref="Existia"/> = false: hay que crear el control.</summary>
    public class CambioControlStock
    {
        public string Almacen { get; set; }
        public bool Existia { get; set; }
        public int? MaximoAnterior { get; set; }
        public int? MinimoAnterior { get; set; }
        public int MaximoNuevo { get; set; }
        public int MinimoNuevo { get; set; }
        public int Multiplos { get; set; } = 1;

        public string Texto() => Existia
            ? $"{Almacen}: máximo {MaximoAnterior} → {MaximoNuevo}, mínimo {MinimoAnterior} → {MinimoNuevo}"
            : $"{Almacen}: control de stock nuevo con máximo {MaximoNuevo} y mínimo {MinimoNuevo}";
    }

    /// <summary>Todo lo que hay que escribir de una decisión, en una transacción.</summary>
    public class RegistroDecisionPropuesta
    {
        public int PropuestaId { get; set; }
        public string Producto { get; set; }
        public string Almacen { get; set; }
        public DateTime Mes { get; set; }
        /// <summary>Lo que había en la fila al leerla (para no pisar una decisión que otra persona acaba de tomar).</summary>
        public string DecisionAnterior { get; set; }
        public DateTime? DecisionFechaAnterior { get; set; }

        public string Decision { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        public string Motivo { get; set; }
        public string MotivoSilencio { get; set; }
        public DateTime? SilenciadaHasta { get; set; }
        public string CanalLiquidacion { get; set; }
        /// <summary>NestoAPI#655: solo al potenciar.</summary>
        public int? ObjetivoVentaMensual { get; set; }
        public DateTime? PotenciarHasta { get; set; }

        /// <summary>Lo que se apunta en las columnas de la fila (el del almacén de la propuesta).</summary>
        public int? StockMaximoAnterior { get; set; }
        public int? StockMinimoAnterior { get; set; }
        public int? StockMaximoAplicado { get; set; }
        public int? StockMinimoAplicado { get; set; }
        public short? EstadoAnterior { get; set; }
        public short? EstadoAplicado { get; set; }

        public List<CambioControlStock> CambiosControlStock { get; set; } = new List<CambioControlStock>();

        /// <summary>Texto con todos los cambios (va al histórico PropuestasStockDecisiones).</summary>
        public string Cambios { get; set; }
    }

    /// <summary>NestoAPI#631: acceso a datos de las decisiones (sustituible en los tests).</summary>
    public interface IRepositorioDecisionesPropuestasStock
    {
        /// <summary>¿Están las columnas y la tabla del corte 3 (Scripts/Issue631_PropuestasStockDecisiones.sql)?</summary>
        bool ExisteEsquema();

        /// <summary>
        /// NestoAPI#655: ¿Están las columnas de «Potenciar ventas» (Scripts/Issue655_PotenciarVentas.sql)? Sin ellas, todo va como
        /// antes y solo la decisión «Potenciar» responde 404.
        /// </summary>
        bool ExistePotenciacion();

        PropuestaStockConDecision LeerPropuesta(int id);

        /// <summary>Filas del mes (sin mes, el último guardado), de más a menos exceso, del estado pedido (sin estado, todas).</summary>
        List<PropuestaStockConDecision> LeerPropuestas(DateTime? mes, string estado, int top);

        /// <summary>Cuántas propuestas hay en cada estado (sin mes, el último guardado).</summary>
        List<ResumenEstadoPropuestas> Resumen(DateTime? mes);

        /// <summary>Productos.Estado ahora (empresa 1); null si el producto no existe.</summary>
        short? LeerEstadoProducto(string producto);

        /// <summary>Los controles de stock del producto (empresa 1) en todos los almacenes.</summary>
        List<ControlStockActual> LeerControlesStock(string producto);

        /// <summary>El valor del parámetro <see cref="MotivosSilencio.CLAVE"/> de la fila «(defecto)» (empresa 1); null si no hay fila.</summary>
        string LeerParametroMotivosSilencio();

        /// <summary>
        /// En una transacción: la decisión en la fila (solo si nadie la ha cambiado desde que se leyó; si no,
        /// <see cref="DecisionPropuestaStockException"/> con 409), los controles de stock, el estado del producto y el histórico.
        /// </summary>
        void GuardarDecision(RegistroDecisionPropuesta registro);
    }

    /// <summary>Un «no» con su código HTTP (400, 404 o 409) y un texto para el usuario.</summary>
    public class DecisionPropuestaStockException : Exception
    {
        public HttpStatusCode Codigo { get; }

        public DecisionPropuestaStockException(HttpStatusCode codigo, string mensaje) : base(mensaje)
        {
            Codigo = codigo;
        }
    }
}
