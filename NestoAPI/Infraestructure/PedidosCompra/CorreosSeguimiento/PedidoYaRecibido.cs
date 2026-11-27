using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>
    /// 27/11/26: cómo está un pedido de compra cuando se le asigna a mano un correo y no tiene líneas enviadas y sin recibir
    /// (<see cref="IRepositorioCorreosSeguimiento.LeerEstadoPedido"/>: una consulta por LinPedidoCmp de esa empresa y número).
    /// </summary>
    public class EstadoPedidoCompraCorreo
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        /// <summary>Todas sus líneas (0 = el pedido no existe).</summary>
        public int Lineas { get; set; }
        /// <summary>-1/1 con Enviado (las que pondrían la fecha del correo).</summary>
        public int LineasEnAire { get; set; }
        /// <summary>-1/1 sin Enviado: aún no se han mandado al proveedor.</summary>
        public int LineasSinEnviar { get; set; }
        /// <summary>Estado 2 o más (albarán o factura).</summary>
        public int LineasRecibidas { get; set; }
        /// <summary>Los días de albarán de sus líneas recibidas, sin repetir.</summary>
        public List<DateTime> Albaranes { get; set; } = new List<DateTime>();
    }

    public enum SituacionPedidoAsignado
    {
        /// <summary>No tiene ninguna línea: no existe (400).</summary>
        NoExiste,
        /// <summary>Tiene líneas enviadas y sin recibir: se le pone la fecha como siempre (no debería llegar aquí).</summary>
        EnElAire,
        /// <summary>Ya se recibió (alguna línea con albarán en el plazo del envío): se asigna sin tocar fechas.</summary>
        YaRecibido,
        /// <summary>Se recibió, pero mucho antes del correo (o mucho después): no puede ser este envío (400).</summary>
        RecibidoFueraDePlazo,
        /// <summary>Nunca se ha enviado al proveedor (ni nada recibido): no está en el aire por otra causa (400 de siempre).</summary>
        NoEnviado,
        /// <summary>Ni en el aire, ni sin enviar, ni recibido (p. ej. líneas en otro estado): 400 de siempre.</summary>
        OtraCausa
    }

    /// <summary>
    /// 27/11/26 (caso real SEUR Id 38, «tu envío de IBEROCEL - PARAISO será entregado hoy», A-2609942, 26/11 12:55: su pedido, el 220985,
    /// se recibió entero antes de que nadie asignara el correo y Asignar daba 400, así que el envío se quedaba «por asignar» para siempre).
    /// Qué es «ya recibido» frente a «no está en el aire por otra causa». PURA. Ya recibido = ninguna línea enviada y sin recibir, alguna
    /// recibida, y algún albarán entre <see cref="DIAS_ANTES_DEL_CORREO"/> días antes del primer correo del envío y
    /// <see cref="DIAS_DESPUES_DEL_CORREO"/> después (el «periodo razonable»: un albarán de un mes antes no es la mercancía que anunciaba el
    /// correo). Las líneas que queden sin enviar no lo impiden (lo enviado ya llegó). Nada recibido y algo sin enviar = nunca se envió.
    /// </summary>
    public static class ReglaPedidoYaRecibido
    {
        /// <summary>El correo a veces llega tarde (la agencia avisa de lo que ya se entregó): una semana de margen hacia atrás.</summary>
        public const int DIAS_ANTES_DEL_CORREO = 7;
        /// <summary>El mismo tope que la entrega estimada que se cree de un correo (60 días).</summary>
        public const int DIAS_DESPUES_DEL_CORREO = 60;

        public static SituacionPedidoAsignado Clasificar(EstadoPedidoCompraCorreo estado, DateTime primerCorreo)
        {
            if (estado == null || estado.Lineas <= 0)
            {
                return SituacionPedidoAsignado.NoExiste;
            }
            if (estado.LineasEnAire > 0)
            {
                return SituacionPedidoAsignado.EnElAire;
            }
            if (estado.LineasRecibidas > 0)
            {
                return AlbaranDelEnvio(estado, primerCorreo).HasValue ? SituacionPedidoAsignado.YaRecibido : SituacionPedidoAsignado.RecibidoFueraDePlazo;
            }
            return estado.LineasSinEnviar > 0 ? SituacionPedidoAsignado.NoEnviado : SituacionPedidoAsignado.OtraCausa;
        }

        /// <summary>
        /// El día de albarán que es el del envío: el primero desde el día del correo (como la medición, <see cref="AsignadorPedidosCompra.DecidirReal"/>)
        /// y, si no hay, el último de antes dentro del margen. Null si ninguno cae en el plazo.
        /// </summary>
        public static DateTime? AlbaranDelEnvio(EstadoPedidoCompraCorreo estado, DateTime primerCorreo)
        {
            DateTime dia = primerCorreo.Date;
            List<DateTime> enPlazo = (estado?.Albaranes ?? new List<DateTime>()).Select(a => a.Date)
                .Where(a => a >= dia.AddDays(-DIAS_ANTES_DEL_CORREO) && a <= dia.AddDays(DIAS_DESPUES_DEL_CORREO)).Distinct().ToList();
            if (!enPlazo.Any())
            {
                return null;
            }
            List<DateTime> desdeElCorreo = enPlazo.Where(a => a >= dia).ToList();
            return desdeElCorreo.Any() ? desdeElCorreo.Min() : enPlazo.Max();
        }

        public static string MensajeAsignado(int pedido) =>
            $"El pedido {pedido} ya estaba recibido: no se ha cambiado ninguna fecha; el envío queda asignado a él.";

        /// <summary>El 400 de lo que no se asigna (null si sí se asigna). El de siempre, con el porqué cuando se sabe.</summary>
        public static string MensajeNoSeAsigna(SituacionPedidoAsignado situacion, EstadoPedidoCompraCorreo estado, int pedido)
        {
            string deSiempre = $"El pedido de compra {pedido} no tiene líneas enviadas y sin recibir: no se le puede poner la fecha del correo.";
            switch (situacion)
            {
                case SituacionPedidoAsignado.NoExiste:
                    return $"No existe el pedido de compra {pedido}.";
                case SituacionPedidoAsignado.RecibidoFueraDePlazo:
                    DateTime? ultimo = estado?.Albaranes?.Any() == true ? estado.Albaranes.Max() : (DateTime?)null;
                    return ultimo.HasValue
                        ? deSiempre + $" Se recibió el {ultimo.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}, demasiado lejos del correo para ser este envío."
                        : deSiempre + " Está recibido, pero sin fecha de albarán: no se puede saber si es este envío.";
                case SituacionPedidoAsignado.YaRecibido:
                    return null;
                default:
                    return deSiempre;
            }
        }
    }
}
