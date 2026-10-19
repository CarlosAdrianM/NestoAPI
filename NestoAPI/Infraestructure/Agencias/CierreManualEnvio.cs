using NestoAPI.Models;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#676 (Sugerencia 673 de Novedades, Aida): «Dar por cerrado» un envío INCIDENTADO que la agencia
    /// nunca cerró (en la pestaña Incidentados seguían saliendo envíos de junio, julio y septiembre).
    ///
    /// <para>Estado al que pasa: ENTREGADO (2). No hay un estado «cerrado a mano», y se ha preferido no crearlo:
    /// un 5 nuevo caería en <c>GatingEntregaFacturas</c> como «≥ DEVUELTO» (retención NO forzable de la remesa),
    /// en los filtros «Estado &lt; ENTREGADO» de los envíos vivos, y sin color ni nombre en las rejillas de Nesto.
    /// Entregado es terminal (el seguimiento automático solo consulta Tramitados e Incidentados), no toca el
    /// reembolso (DEVUELTO lo quitaría solo, Sugerencia 544) ni sale en Retrasados (solo Tramitados), y la
    /// pestaña Reembolsos sigue mostrándolo mientras la agencia no lo pague (Estado ≥ TRAMITADO).</para>
    ///
    /// <para>Lo que lo distingue de una entrega confirmada: <see cref="DETALLE_CERRADO_A_MANO"/> en DetalleEstado,
    /// FechaEntrega sin tocar (no hay fecha real de entrega) y las filas de EnviosHistoria (estado y texto de
    /// la incidencia anteriores, usuario, fecha y motivo).</para>
    /// </summary>
    public static class CierreManualEnvio
    {
        /// <summary>Lo que queda en DetalleEstado (varchar(100)) en lugar del texto de la incidencia.</summary>
        public const string DETALLE_CERRADO_A_MANO = "Cerrado a mano, sin confirmación de la agencia";

        public const string MENSAJE_FALTA_MOTIVO = "Indique el motivo por el que da por cerrado el envío.";

        /// <summary>EnviosHistoria.ValorAnterior es nvarchar(50) NOT NULL.</summary>
        private const int LONGITUD_VALOR_ANTERIOR = 50;

        /// <summary>El motivo de error para el usuario (de usted), o null si se puede dar por cerrado.</summary>
        public static string Validar(EnviosAgencia envio, string motivo)
        {
            if (string.IsNullOrWhiteSpace(motivo))
            {
                return MENSAJE_FALTA_MOTIVO;
            }
            if (envio.Estado != Constantes.Agencias.ESTADO_INCIDENTADO)
            {
                return $"Solo se puede dar por cerrado un envío incidentado. El envío {envio.Numero}" +
                    (envio.Pedido.HasValue ? $" (pedido {envio.Pedido})" : string.Empty) +
                    " ya no lo está: actualice la lista.";
            }
            return null;
        }

        /// <summary>
        /// Pasa el envío a Entregado con el detalle de cierre a mano y devuelve las filas de historia (una por
        /// campo, con el valor ANTERIOR, como <c>TramitacionEnviosService.ConstruirHistoria</c>).
        /// </summary>
        public static List<EnvioHistoria> Aplicar(EnviosAgencia envio, string motivo, string usuario, DateTime ahora)
        {
            string observaciones = $"Dado por cerrado a mano: {motivo.Trim()}";
            var historia = new List<EnvioHistoria>
            {
                Fila(envio, "Estado", envio.Estado.ToString(), observaciones, usuario, ahora),
                Fila(envio, "DetalleEstado", envio.DetalleEstado?.Trim() ?? string.Empty, observaciones, usuario, ahora)
            };
            envio.Estado = Constantes.Agencias.ESTADO_ENTREGADO;
            envio.DetalleEstado = DETALLE_CERRADO_A_MANO;
            envio.Usuario = usuario;
            return historia;
        }

        /// <summary>¿Lo cerró una persona a mano (y no la agencia)?</summary>
        public static bool EstaCerradoAMano(EnviosAgencia envio) =>
            envio != null
            && envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO
            && envio.DetalleEstado?.Trim() == DETALLE_CERRADO_A_MANO;

        private static EnvioHistoria Fila(EnviosAgencia envio, string campo, string valorAnterior, string observaciones, string usuario, DateTime ahora)
            => new EnvioHistoria
            {
                NumeroEnvio = envio.Numero,
                Campo = campo,
                ValorAnterior = valorAnterior.Length <= LONGITUD_VALOR_ANTERIOR ? valorAnterior : valorAnterior.Substring(0, LONGITUD_VALOR_ANTERIOR),
                Observaciones = observaciones,
                Usuario = usuario,
                FechaModificacion = ahora
            };
    }
}
