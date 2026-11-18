using NestoAPI.Models.PedidosVenta;
using System;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#693: qué hacer con un pedido después de recalcular su fecha de entrega a la agencia.</summary>
    public enum AccionPromesaRiesgo
    {
        /// <summary>Cumple su fecha y no estaba registrado: nada.</summary>
        Nada,
        /// <summary>Se retrasa o se queda sin fecha y no estaba registrado: se registra y se avisa.</summary>
        Registrar,
        /// <summary>Ya estaba registrado y ahora va peor (más tarde, o se queda sin fecha): se actualiza y se vuelve a avisar.</summary>
        Empeora,
        /// <summary>Ya estaba registrado y sigue igual (o algo mejor sin llegar a cumplir): se actualiza el texto, sin avisar.</summary>
        Actualizar,
        /// <summary>Estaba registrado y vuelve a cumplir la fecha, se ha servido o la fecha se ha renegociado.</summary>
        Resolver,
        /// <summary>
        /// Carlos (18/11/26): estaba registrado, sigue saliendo después de la prometida pero ya no falta nada (le ha llegado lo que
        /// faltaba: sale en el próximo picking). Se resuelve con la buena noticia («Faltaba X; llegó el 17/11. Sale hoy.») y, si ya se
        /// le había avisado, se le dice una sola vez.
        /// </summary>
        YaLlego
    }

    public class DecisionPromesaRiesgo
    {
        public AccionPromesaRiesgo Accion { get; set; }
        /// <summary>Solo con <see cref="AccionPromesaRiesgo.Resolver"/>.</summary>
        public string MotivoResolucion { get; set; }
        /// <summary>
        /// Con la fecha renegociada, además de resolver la fila vieja hay que mirar la nueva promesa como si fuera la primera
        /// vez: esto dice qué hacer con ella (Nada o Registrar).
        /// </summary>
        public AccionPromesaRiesgo AccionTrasRenegociar { get; set; } = AccionPromesaRiesgo.Nada;
    }

    /// <summary>
    /// NestoAPI#693: la regla, pura. La fecha nueva es la que el motor de #606 dice HOY (<see cref="FechaEntregaAgenciaDTO.FechaEntregaAgencia"/>,
    /// la completa en «Todo junto» y la primera en el resto, la misma que se prometió al crear el pedido).
    /// <list type="bullet">
    /// <item>En riesgo: sin fecha, o con fecha posterior a la prometida. Un pedido sin nada que salga de Algete por agencia (sin
    /// desglose, p. ej. de tienda) no está en riesgo.</item>
    /// <item>Un pedido avisado no se vuelve a avisar salvo que empeore: de fecha a sin fecha, o a una fecha más tarde que la
    /// registrada.</item>
    /// <item>Se resuelve solo cuando vuelve a cumplir la fecha, se sirve (no queda nada pendiente) o la fecha se renegocia (la
    /// prometida del pedido ya no es la registrada).</item>
    /// </list>
    /// </summary>
    public static class EvaluadorPromesaRiesgo
    {
        public const string RESUELTO_CUMPLE = "Vuelve a cumplir la fecha prometida";
        public const string RESUELTO_SERVIDO = "Servido (no queda nada pendiente)";
        public const string RESUELTO_RENEGOCIADO = "Fecha prometida renegociada";
        public const string RESUELTO_LLEGO = "Ha llegado lo que faltaba";

        /// <summary>
        /// Carlos (18/11/26): en riesgo Y con algo que contar al vendedor: algún culpable (lo que no está listo en Algete y sale después de
        /// la prometida) o la fecha de entrega del pedido, que lo retiene. Un pedido que lo tiene todo en Algete (o en picking) y sale en el
        /// siguiente picking ya no es un problema: no se registra ni se avisa (antes se avisaba «todo está listo, pero no salió el …»).
        /// </summary>
        public static bool MereceAviso(DateTime prometida, FechaEntregaAgenciaDTO calculo) =>
            EstaEnRiesgo(prometida, calculo)
            && (TextosPromesaRiesgo.Culpables(prometida, calculo).Any() || TextosPromesaRiesgo.TieneFechaEntregaPosterior(calculo));

        /// <summary>True si con este cálculo el pedido ya no llega a la fecha prometida.</summary>
        public static bool EstaEnRiesgo(DateTime prometida, FechaEntregaAgenciaDTO calculo)
        {
            if (calculo == null)
            {
                return false;
            }
            DateTime? nueva = calculo.FechaEntregaAgencia?.Date;
            if (!nueva.HasValue)
            {
                return calculo.Desglose != null && calculo.Desglose.Any();
            }
            return nueva.Value > prometida.Date;
        }

        /// <summary>True si <paramref name="nueva"/> es peor que lo registrado (más tarde, o sin fecha cuando tenía).</summary>
        public static bool Empeora(DateTime? registrada, DateTime? nueva)
        {
            if (!registrada.HasValue)
            {
                return false; // ya estaba sin fecha: no hay nada peor
            }
            return !nueva.HasValue || nueva.Value.Date > registrada.Value.Date;
        }

        /// <param name="prometida">La prometida que tiene HOY el pedido (null si ya no tiene).</param>
        /// <param name="calculo">El cálculo de ahora. Null si el pedido ya no existe.</param>
        /// <param name="quedaPendiente">Si le queda algo pendiente de servir.</param>
        /// <param name="registrado">La fila sin resolver, o null.</param>
        public static DecisionPromesaRiesgo Decidir(DateTime? prometida, FechaEntregaAgenciaDTO calculo, bool quedaPendiente, RiesgoPromesa registrado)
        {
            if (registrado != null)
            {
                if (calculo == null || !quedaPendiente)
                {
                    return Resolver(RESUELTO_SERVIDO);
                }
                if (!prometida.HasValue || prometida.Value.Date != registrado.FechaPrometida.Date)
                {
                    var renegociada = Resolver(RESUELTO_RENEGOCIADO);
                    renegociada.AccionTrasRenegociar = prometida.HasValue && MereceAviso(prometida.Value, calculo)
                        ? AccionPromesaRiesgo.Registrar
                        : AccionPromesaRiesgo.Nada;
                    return renegociada;
                }
                if (!EstaEnRiesgo(prometida.Value, calculo))
                {
                    return Resolver(RESUELTO_CUMPLE);
                }
                if (!MereceAviso(prometida.Value, calculo))
                {
                    return new DecisionPromesaRiesgo { Accion = AccionPromesaRiesgo.YaLlego, MotivoResolucion = RESUELTO_LLEGO };
                }
                return new DecisionPromesaRiesgo
                {
                    Accion = Empeora(registrado.FechaNueva, calculo.FechaEntregaAgencia) ? AccionPromesaRiesgo.Empeora : AccionPromesaRiesgo.Actualizar
                };
            }
            if (!prometida.HasValue || calculo == null || !quedaPendiente || !MereceAviso(prometida.Value, calculo))
            {
                return new DecisionPromesaRiesgo { Accion = AccionPromesaRiesgo.Nada };
            }
            return new DecisionPromesaRiesgo { Accion = AccionPromesaRiesgo.Registrar };
        }

        private static DecisionPromesaRiesgo Resolver(string motivo) =>
            new DecisionPromesaRiesgo { Accion = AccionPromesaRiesgo.Resolver, MotivoResolucion = motivo };
    }
}
