using System;
using System.Collections.Generic;
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

        public static readonly string[] TODAS = { APLICAR, RECHAZAR, POSPONER, SILENCIAR };

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

        public static readonly string[] TODOS = { PENDIENTE, APLICADA, RECHAZADA, POSPUESTA, SILENCIADA, SUPERADA, SIN_ACCION };

        public static string Normalizar(string estado)
            => estado == null ? null : Array.Find(TODOS, e => string.Equals(e, estado.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>NestoAPI#631: por qué se silencia (los tres de la issue).</summary>
    public static class MotivosSilencio
    {
        public const string NUEVO = "Nuevo";
        public const string ESTACIONAL = "Estacional";
        public const string APARATO = "Aparato";

        public static readonly string[] TODOS = { NUEVO, ESTACIONAL, APARATO };

        public static string Normalizar(string motivo)
            => motivo == null ? null : Array.Find(TODOS, m => string.Equals(m, motivo.Trim(), StringComparison.OrdinalIgnoreCase));
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
        /// <summary>Solo al silenciar: Nuevo, Estacional o Aparato.</summary>
        public string MotivoSilencio { get; set; }
        /// <summary>Solo al silenciar: hasta cuándo (incluido) no vuelve a salir como pendiente.</summary>
        public DateTime? SilenciarHasta { get; set; }
        /// <summary>Solo al aplicar un «Liquidar»: Outlet o Ganavisiones (opcional; se apunta, no se hace nada automático).</summary>
        public string CanalLiquidacion { get; set; }
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

        PropuestaStockConDecision LeerPropuesta(int id);

        /// <summary>Filas del mes (sin mes, el último guardado), de más a menos exceso, del estado pedido (sin estado, todas).</summary>
        List<PropuestaStockConDecision> LeerPropuestas(DateTime? mes, string estado, int top);

        /// <summary>Cuántas propuestas hay en cada estado (sin mes, el último guardado).</summary>
        List<ResumenEstadoPropuestas> Resumen(DateTime? mes);

        /// <summary>Productos.Estado ahora (empresa 1); null si el producto no existe.</summary>
        short? LeerEstadoProducto(string producto);

        /// <summary>Los controles de stock del producto (empresa 1) en todos los almacenes.</summary>
        List<ControlStockActual> LeerControlesStock(string producto);

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
