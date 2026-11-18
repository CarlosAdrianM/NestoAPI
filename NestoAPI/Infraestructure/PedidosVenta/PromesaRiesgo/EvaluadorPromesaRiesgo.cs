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
        /// faltaba: sale en el próximo picking). Se resuelve con la buena noticia («Faltaba X; llegó el martes 17/11. Sale el martes
        /// 17/11.») y, si ya se le había avisado, se le dice una sola vez. Después se queda en observación.
        /// </summary>
        YaLlego,
        /// <summary>
        /// Carlos (18/11/26): va tarde con todo en Algete y no estaba registrado: fila «en observación» (sin aviso) para saber desde cuándo.
        /// </summary>
        Observar,
        /// <summary>
        /// En observación y ya ha pasado un día laborable completo de Algete sin salir: se avisa al vendedor («Está todo en Algete desde
        /// el lunes 16/11 y sigue sin salir…»), una vez (y otra si empeora, como el resto).
        /// </summary>
        SigueSinSalir
    }

    public class DecisionPromesaRiesgo
    {
        public AccionPromesaRiesgo Accion { get; set; }
        /// <summary>Con <see cref="AccionPromesaRiesgo.Resolver"/> y <see cref="AccionPromesaRiesgo.YaLlego"/>.</summary>
        public string MotivoResolucion { get; set; }
        /// <summary>
        /// Lo que hay que hacer DESPUÉS de resolver la fila vieja (Nada, Registrar u Observar): con la fecha renegociada se mira la
        /// nueva promesa como si fuera la primera vez; al llegar lo que faltaba o al acabar la observación, igual.
        /// </summary>
        public AccionPromesaRiesgo AccionTrasRenegociar { get; set; } = AccionPromesaRiesgo.Nada;
    }

    /// <summary>
    /// NestoAPI#693: la regla, pura. La fecha nueva es la que el motor de #606 dice HOY (<see cref="FechaEntregaAgenciaDTO.FechaEntregaAgencia"/>,
    /// la completa en «Todo junto» y la primera en el resto, la misma que se prometió al crear el pedido).
    /// <list type="bullet">
    /// <item>En riesgo: sin fecha, o con fecha posterior a la prometida. Un pedido sin nada que salga de Algete por agencia (sin
    /// desglose, p. ej. de tienda) no está en riesgo.</item>
    /// <item>Se avisa en el acto si falta algo fuera de Algete o lo retiene su fecha de entrega (<see cref="MereceAviso"/>). Si lo tiene todo en
    /// Algete, se observa y solo se avisa si pasa un día laborable completo de Algete sin salir (<see cref="ObservacionVencida"/>).</item>
    /// <item>Un pedido avisado no se vuelve a avisar salvo que empeore: de fecha a sin fecha, o a una fecha más tarde que la
    /// registrada.</item>
    /// <item>Se resuelve solo cuando vuelve a cumplir la fecha, se sirve (no queda nada pendiente), la fecha se renegocia (la
    /// prometida del pedido ya no es la registrada) o llega lo que faltaba.</item>
    /// </list>
    /// </summary>
    public static class EvaluadorPromesaRiesgo
    {
        public const string RESUELTO_CUMPLE = "Vuelve a cumplir la fecha prometida";
        public const string RESUELTO_SERVIDO = "Servido (no queda nada pendiente)";
        public const string RESUELTO_RENEGOCIADO = "Fecha prometida renegociada";
        public const string RESUELTO_LLEGO = "Ha llegado lo que faltaba";
        public const string RESUELTO_FIN_OBSERVACION = "Fin de la observación: ya falta algo";

        /// <summary>
        /// Carlos (18/11/26): en riesgo Y con algo que contar al vendedor en el acto: algún culpable (lo que no está listo en Algete y sale
        /// después de la prometida) o la fecha de entrega del pedido, que lo retiene. Lo que lo tiene todo en Algete (o en picking) normalmente
        /// sale en el siguiente picking: no se avisa en el acto (se observa).
        /// </summary>
        public static bool MereceAviso(DateTime prometida, FechaEntregaAgenciaDTO calculo) =>
            EstaEnRiesgo(prometida, calculo)
            && (TextosPromesaRiesgo.Culpables(prometida, calculo).Any() || TextosPromesaRiesgo.TieneFechaEntregaPosterior(calculo));

        /// <summary>
        /// Carlos (18/11/26): «pasa el tiempo» = ha terminado entero el primer día laborable de Algete posterior al día en que se vio todo en
        /// Algete (ya hubo un picking completo al que podía entrar). Listo el viernes 13/11 → laborable el lunes 16/11 → vence el martes
        /// 17/11; con el lunes festivo, el miércoles. <paramref name="esFestivoAlgete"/> cuenta fines de semana y festivos.
        /// </summary>
        public static bool ObservacionVencida(DateTime desde, DateTime ahora, Func<DateTime, bool> esFestivoAlgete)
        {
            Func<DateTime, bool> festivo = esFestivoAlgete ?? (d => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday);
            DateTime laborable = desde.Date.AddDays(1);
            for (int i = 0; i < 30 && festivo(laborable); i++)
            {
                laborable = laborable.AddDays(1);
            }
            return ahora.Date > laborable;
        }

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
        /// <param name="registrado">La fila sin resolver (avisada o en observación), o null.</param>
        /// <param name="observacionVencida">Solo con una fila en observación: si ya ha pasado el día laborable (<see cref="ObservacionVencida"/>).</param>
        public static DecisionPromesaRiesgo Decidir(DateTime? prometida, FechaEntregaAgenciaDTO calculo, bool quedaPendiente, RiesgoPromesa registrado,
            bool observacionVencida = false)
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
                    renegociada.AccionTrasRenegociar = prometida.HasValue ? PrimeraVez(prometida.Value, calculo) : AccionPromesaRiesgo.Nada;
                    return renegociada;
                }
                if (!EstaEnRiesgo(prometida.Value, calculo))
                {
                    return Resolver(RESUELTO_CUMPLE);
                }
                bool merece = MereceAviso(prometida.Value, calculo);
                if (registrado.EnObservacion)
                {
                    if (merece)
                    {
                        // Ya no está todo en Algete: deja de observarse y se registra como un riesgo normal (con su culpable).
                        var fin = Resolver(RESUELTO_FIN_OBSERVACION);
                        fin.AccionTrasRenegociar = AccionPromesaRiesgo.Registrar;
                        return fin;
                    }
                    return new DecisionPromesaRiesgo { Accion = observacionVencida ? AccionPromesaRiesgo.SigueSinSalir : AccionPromesaRiesgo.Nada };
                }
                if (!merece && !registrado.TodoListoDesde.HasValue)
                {
                    return new DecisionPromesaRiesgo
                    {
                        Accion = AccionPromesaRiesgo.YaLlego,
                        MotivoResolucion = RESUELTO_LLEGO,
                        AccionTrasRenegociar = AccionPromesaRiesgo.Observar
                    };
                }
                if (!merece)
                {
                    // El aviso de «sigue sin salir» (TodoListoDesde): su fecha se corre un día cada día que no sale; eso no es empeorar
                    // (sería un aviso diario). Se actualiza sin avisar; si aparece un culpable, se mira como un riesgo normal.
                    return new DecisionPromesaRiesgo { Accion = AccionPromesaRiesgo.Actualizar };
                }
                // Riesgo normal: no se vuelve a avisar salvo que empeore.
                return new DecisionPromesaRiesgo
                {
                    Accion = Empeora(registrado.FechaNueva, calculo.FechaEntregaAgencia) ? AccionPromesaRiesgo.Empeora : AccionPromesaRiesgo.Actualizar
                };
            }
            if (!prometida.HasValue || calculo == null || !quedaPendiente)
            {
                return new DecisionPromesaRiesgo { Accion = AccionPromesaRiesgo.Nada };
            }
            return new DecisionPromesaRiesgo { Accion = PrimeraVez(prometida.Value, calculo) };
        }

        /// <summary>Sin fila: Registrar si merece aviso, Observar si va tarde con todo en Algete, si no Nada.</summary>
        private static AccionPromesaRiesgo PrimeraVez(DateTime prometida, FechaEntregaAgenciaDTO calculo)
        {
            if (!EstaEnRiesgo(prometida, calculo))
            {
                return AccionPromesaRiesgo.Nada;
            }
            return MereceAviso(prometida, calculo) ? AccionPromesaRiesgo.Registrar : AccionPromesaRiesgo.Observar;
        }

        private static DecisionPromesaRiesgo Resolver(string motivo) =>
            new DecisionPromesaRiesgo { Accion = AccionPromesaRiesgo.Resolver, MotivoResolucion = motivo };
    }
}
