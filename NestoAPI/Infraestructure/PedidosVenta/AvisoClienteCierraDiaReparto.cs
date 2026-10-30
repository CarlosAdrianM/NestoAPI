using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#764 (Carlos 30/10/26): aviso NO bloqueante al grabar un pedido que va por ruta interna cuando el cliente cierra
    /// (Clientes.DiasEnServir, <see cref="GestorDiasEnServir"/>) justo el día de reparto de su código postal. Sin aviso, el
    /// picking lo retiraría cada semana por el cierre y no saldría nunca por nuestra ruta.
    /// </summary>
    public static class AvisoClienteCierraDiaReparto
    {
        public const string TIPO = "ClienteCierraDiaReparto";

        /// <summary>Un lunes cualquiera, para mirar los días de servir por día de la semana.</summary>
        private static readonly DateTime LUNES_REFERENCIA = new DateTime(2024, 1, 1);

        /// <summary>
        /// «El cliente cierra el martes, que es su día de reparto por nuestra ruta: envíelo por agencia o cambie el día.», o null
        /// si no hay nada que avisar (no va por ruta interna, su código postal no tiene día o el cliente abre ese día).
        /// </summary>
        public static string Mensaje(string ruta, string codigoPostal, string diasEnServir)
        {
            DayOfWeek? dia = DiasRepartoCodigosPostales.DiaRutaInterna(ruta, codigoPostal);
            if (!dia.HasValue)
            {
                return null;
            }
            DateTime fecha = LUNES_REFERENCIA.AddDays(((int)dia.Value + 6) % 7);
            if (GestorDiasEnServir.EstaAbierto(diasEnServir, fecha))
            {
                return null;
            }
            return $"El cliente cierra el {CalendarioRutasDia.Nombre(dia.Value)}, que es su día de reparto por nuestra ruta: " +
                "envíelo por agencia o cambie el día.";
        }

        /// <summary>El aviso para la respuesta del POST/PUT del pedido, o null.</summary>
        public static AvisoPedidoDTO Crear(string ruta, string codigoPostal, string diasEnServir)
        {
            string mensaje = Mensaje(ruta, codigoPostal, diasEnServir);
            return mensaje == null
                ? null
                : new AvisoPedidoDTO
                {
                    Tipo = TIPO,
                    Mensaje = mensaje,
                    Datos = new { DiaReparto = CalendarioRutasDia.Nombre(DiasRepartoCodigosPostales.DiaRutaInterna(ruta, codigoPostal).Value) }
                };
        }
    }
}
