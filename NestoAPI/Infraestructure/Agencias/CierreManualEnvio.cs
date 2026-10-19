using NestoAPI.Models;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#676 (Sugerencia 673 de Novedades, Aida): «Dar por cerrado» un envío INCIDENTADO que la agencia
    /// nunca cerró (en la pestaña Incidentados seguían saliendo envíos de junio, julio y septiembre).
    ///
    /// <para>Estado al que pasa: su propio <see cref="Constantes.Agencias.ESTADO_CERRADO_A_MANO"/> (5), decisión de
    /// Carlos (19/10/26): no es una entrega confirmada y no debe parecerlo. Se comporta como un envío TERMINADO sin
    /// confirmar: no está vivo (ni Incidentados, ni Tramitados, ni Retrasados, ni el seguimiento automático), no
    /// retiene la remesa (<c>GatingEntregaFacturas</c> lo trata como entregado), sigue en Reembolsos mientras la
    /// agencia no pague y en Mis pedidos el cliente lo ve como «Envío cerrado». Como queda POR ENCIMA de DEVUELTO
    /// (4), cualquier filtro «≥ DEVUELTO» o «≥ TRAMITADO» nuevo tiene que decidir qué hace con él.</para>
    ///
    /// <para>Además: <see cref="DETALLE_CERRADO_A_MANO"/> en DetalleEstado, FechaEntrega sin tocar (no hay fecha real
    /// de entrega) y las filas de EnviosHistoria (estado y texto de la incidencia anteriores, usuario, fecha y
    /// motivo). Si la agencia confirma después la entrega, pasa a Entregado con su fecha.</para>
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
            envio.Estado = Constantes.Agencias.ESTADO_CERRADO_A_MANO;
            envio.DetalleEstado = DETALLE_CERRADO_A_MANO;
            envio.Usuario = usuario;
            return historia;
        }

        /// <summary>¿Lo cerró una persona a mano (y no la agencia)?</summary>
        public static bool EstaCerradoAMano(EnviosAgencia envio) =>
            envio != null && envio.Estado == Constantes.Agencias.ESTADO_CERRADO_A_MANO;

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
