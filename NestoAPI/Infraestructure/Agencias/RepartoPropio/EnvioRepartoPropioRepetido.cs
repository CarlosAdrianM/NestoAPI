using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>NestoAPI#847: la respuesta 409 cuando el pedido ya tiene su envío del reparto propio (para que el cliente lo reimprima).</summary>
    public class EnvioRepartoPropioRepetidoDTO
    {
        public string Codigo { get; set; } = EnvioRepartoPropioRepetido.CODIGO;
        public string Message { get; set; }
        /// <summary>El envío que ya tiene el pedido (el que hay que reimprimir).</summary>
        public int Envio { get; set; }
        public string CodigoBarras { get; set; }
        public short Bultos { get; set; }
        public short Estado { get; set; }
    }

    /// <summary>
    /// NestoAPI#847 (sugerencia 1103, Alfredo, 06/11/26): la etiqueta del envío 250662 (pedido 929042, cliente 1179) no salió
    /// y se volvió a pulsar «Insertar e Imprimir» dos veces: tres envíos del reparto propio para el mismo pedido. La ampliación
    /// de Agencias solo busca envíos en curso (Estado 0) y el del reparto propio queda tramitado (Estado 1) en cuanto se
    /// imprime, así que cada pulsación creaba otro. La regla: si el pedido ya tiene un envío del reparto propio sin tramitar
    /// (en curso) o tramitado y sin entregar con fecha de hoy o posterior (el Tramitar le pone el día de reparto), no se crea
    /// otro: se devuelve ese para reimprimirlo. Las etiquetas pendientes (Estado -1) no cuentan: las recoge Agencias aparte.
    /// </summary>
    public static class EnvioRepartoPropioRepetido
    {
        public const string CODIGO = "ENVIO_REPARTO_PROPIO_REPETIDO";

        /// <summary>Solo un envío nuevo (no una etiqueta pendiente) del reparto propio de un pedido.</summary>
        public static bool AplicaA(EnviosAgencia nuevo)
            => nuevo != null && nuevo.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO && nuevo.Pedido.HasValue && nuevo.Pedido.Value > 0
               && nuevo.Estado >= Constantes.Agencias.ESTADO_EN_CURSO;

        /// <summary>¿Este envío ya existente es el que hay que reimprimir en vez de crear otro?</summary>
        public static bool EsElMismoEnvio(EnviosAgencia existente, string empresa, int pedido, DateTime hoy)
            => existente != null && existente.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO
               && existente.Pedido == pedido
               && string.Equals(existente.Empresa?.Trim(), empresa?.Trim(), StringComparison.OrdinalIgnoreCase)
               && (existente.Estado == Constantes.Agencias.ESTADO_EN_CURSO || existente.Estado == Constantes.Agencias.ESTADO_TRAMITADO)
               && existente.Fecha.Date >= hoy.Date;

        /// <summary>El envío que ya tiene el pedido (el más antiguo si hubiera varios), o null.</summary>
        public static EnviosAgencia Buscar(IEnumerable<EnviosAgencia> candidatos, string empresa, int pedido, DateTime hoy)
            => (candidatos ?? Enumerable.Empty<EnviosAgencia>())
                .Where(e => EsElMismoEnvio(e, empresa, pedido, hoy))
                .OrderBy(e => e.Numero)
                .FirstOrDefault();

        public static string Mensaje(EnviosAgencia existente)
            => $"El pedido {existente.Pedido} ya tiene el envío {existente.Numero} del reparto propio " +
               $"({existente.Bultos} {(existente.Bultos == 1 ? "bulto" : "bultos")}): no se crea otro. Para volver a sacar la etiqueta, " +
               $"reimprima la del envío {existente.Numero}; si hacen falta más bultos, modifique ese envío.";

        public static EnvioRepartoPropioRepetidoDTO Respuesta(EnviosAgencia existente) => new EnvioRepartoPropioRepetidoDTO
        {
            Message = Mensaje(existente),
            Envio = existente.Numero,
            CodigoBarras = existente.CodigoBarras?.Trim(),
            Bultos = existente.Bultos,
            Estado = existente.Estado
        };
    }
}
