using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta.ConsultaReembolso
{
    /// <summary>NestoAPI#734: los textos que ve la gente (de usted) y los datos de las notificaciones, en un solo sitio.</summary>
    public static class TextosConsultaReembolso
    {
        /// <summary>NotificacionPushDTO.Tipo y Datos["tipo"] de los avisos de la consulta (campana de Nesto y Avisos de Ariadna).</summary>
        public const string TIPO_NOTIFICACION = "ConsultaReembolso";
        /// <summary>Datos["accion"]: la pregunta a Administración.</summary>
        public const string ACCION_CONSULTA = "consulta";
        /// <summary>Datos["accion"]: la respuesta a quien preguntó.</summary>
        public const string ACCION_RESPUESTA = "respuesta";

        public const string ERROR_SIN_TABLA = "Falta la tabla ConsultasReembolsoPedido (Scripts/Issue734_ConsultasReembolso.sql). Avise a Informática.";
        public const string ERROR_SIN_ENLACES = "El pedido no tiene enlaces de pago pendientes: no hace falta consultar a Administración.";
        public const string ERROR_SIN_DECISION = "Indique si el pedido sale con reembolso o sin reembolso (ConReembolso).";
        public const string ERROR_SIN_FORMA_PAGO = "Para que salga sin reembolso, indique la forma de pago pactada con el cliente (recibo bancario, transferencia…).";
        public const string ERROR_SIN_CONSULTA = "El pedido no tiene ninguna consulta de reembolso abierta.";
        public const string ERROR_SIN_PERMISO_PREGUNTAR = "Solo Almacén, Tiendas o Dirección pueden consultar a Administración si un pedido sale con reembolso.";
        public const string ERROR_SIN_PERMISO_RESPONDER = "Solo Administración o Dirección pueden contestar si un pedido sale con o sin reembolso.";
        public const string ERROR_ENLACES_A_ANULAR = "Los enlaces que quiere anular no son enlaces pendientes de este pedido: {0}.";
        public const string AVISO_SIN_ADMINISTRACION = "No se ha podido avisar a Administración por la campana: llámeles para que contesten la consulta.";
        public const string AVISO_SIN_RESPUESTA_AVISADA = "No se ha podido avisar a quien preguntó: dígaselo usted.";

        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        public static string Importe(decimal importe) => importe.ToString("C", Espanol);

        public static string Fecha(System.DateTime fecha) => fecha.ToString("dd/MM/yyyy", Espanol);

        /// <summary>«un enlace de pago pendiente de 252,94 € (enviado el 16/10/2026)» / «2 enlaces de pago pendientes (300,00 € en total)».</summary>
        public static string DescripcionEnlaces(IReadOnlyCollection<EnlacePagoPedido> pendientes)
        {
            List<EnlacePagoPedido> lista = (pendientes ?? new List<EnlacePagoPedido>()).Where(e => e != null).ToList();
            if (lista.Count == 0)
            {
                return "ningún enlace de pago pendiente";
            }
            if (lista.Count == 1)
            {
                return $"un enlace de pago pendiente de {Importe(lista[0].Importe)} (enviado el {Fecha(lista[0].FechaCreacion)})";
            }
            return $"{lista.Count} enlaces de pago pendientes ({Importe(lista.Sum(e => e.Importe))} en total)";
        }

        /// <summary>El motivo de la retención (#698): «Enlace de pago pendiente de 252,94 €: consultando a Administración (#734)».</summary>
        public static string MotivoRetencion(IReadOnlyCollection<EnlacePagoPedido> pendientes)
        {
            List<EnlacePagoPedido> lista = (pendientes ?? new List<EnlacePagoPedido>()).ToList();
            string enlaces = lista.Count == 1
                ? $"Enlace de pago pendiente de {Importe(lista[0].Importe)}"
                : $"{lista.Count} enlaces de pago pendientes ({Importe(lista.Sum(e => e.Importe))})";
            return $"{enlaces}: consultando a Administración si sale con o sin reembolso (#734)";
        }

        /// <summary>El motivo de la liberación (#698) con la respuesta.</summary>
        public static string MotivoLiberacion(ConsultaReembolsoDTO consulta)
            => $"Administración ({RetencionNombre(consulta.UsuarioRespuesta)}): {Decision(consulta)} (#734)";

        /// <summary>«sale con reembolso» / «sale sin reembolso (RCB, CONTADO)».</summary>
        public static string Decision(ConsultaReembolsoDTO consulta)
        {
            if (consulta?.ConReembolso == true)
            {
                return "sale con reembolso";
            }
            string forma = string.IsNullOrWhiteSpace(consulta?.FormaPago) ? null : consulta.FormaPago.Trim();
            string plazos = string.IsNullOrWhiteSpace(consulta?.PlazosPago) ? null : consulta.PlazosPago.Trim();
            string detalle = forma == null ? string.Empty : plazos == null ? $" ({forma})" : $" ({forma}, {plazos})";
            return $"sale sin reembolso{detalle}";
        }

        /// <summary>El aviso de la propuesta de envío mientras Administración no contesta.</summary>
        public static string AvisoPropuestaAbierta(ConsultaReembolsoDTO consulta)
        {
            return $"El pedido está retenido: {RetencionNombre(consulta.Usuario)} consultó a Administración el {Fecha(consulta.Fecha)} si sale con o sin reembolso " +
                $"(enlace de pago pendiente de {Importe(consulta.ImporteEnlaces)}). Espere a que contesten.";
        }

        /// <summary>Campana de Nesto al grupo Administración: hay que decidir.</summary>
        public static NotificacionPushDTO NotificacionConsulta(ConsultaReembolsoDTO consulta, DatosPagoPedidoConsulta pedido, IReadOnlyCollection<EnlacePagoPedido> pendientes)
        {
            string comentario = string.IsNullOrWhiteSpace(consulta.Comentario) ? string.Empty : $" Comentario: «{consulta.Comentario.Trim()}».";
            string lista = string.Join("; ", (pendientes ?? new List<EnlacePagoPedido>())
                .Select(e => $"{Importe(e.Importe)} del {Fecha(e.FechaCreacion)}{(string.IsNullOrWhiteSpace(e.Descripcion) ? string.Empty : $" («{e.Descripcion.Trim()}»)")}"));
            return new NotificacionPushDTO
            {
                Titulo = $"Pedido {consulta.Pedido}: ¿sale con o sin reembolso?",
                Cuerpo = $"{RetencionNombre(consulta.Usuario)} ha retenido el pedido {consulta.Pedido}{TextoCliente(pedido)} al ir a hacer el envío: va contra " +
                    $"reembolso y tiene {DescripcionEnlaces(pendientes)}. Enlaces: {lista}.{comentario} Conteste si sale con reembolso (y si se anulan " +
                    "los enlaces) o sin reembolso (con la forma de pago pactada). Hasta que conteste, el pedido no sale.",
                Tipo = TIPO_NOTIFICACION,
                Datos = Datos(consulta, pedido, ACCION_CONSULTA)
            };
        }

        /// <summary>Campana de Nesto y Avisos de Ariadna a quien preguntó: «Administración (Fulano) dice: sale con/sin reembolso».</summary>
        public static NotificacionPushDTO NotificacionRespuesta(ConsultaReembolsoDTO consulta, DatosPagoPedidoConsulta pedido, IReadOnlyCollection<int> anulados, bool liberado)
        {
            string enlaces = anulados != null && anulados.Count > 0
                ? (anulados.Count == 1 ? " Se ha anulado el enlace de pago pendiente." : $" Se han anulado los {anulados.Count} enlaces de pago pendientes.")
                : string.Empty;
            string comentario = string.IsNullOrWhiteSpace(consulta.ComentarioRespuesta) ? string.Empty : $" Comentario: «{consulta.ComentarioRespuesta.Trim()}».";
            string sigue = liberado
                ? " El pedido ya no está retenido: puede hacer el envío."
                : " El pedido sigue retenido por otro motivo: libérelo desde el pedido si tiene que salir.";
            var datos = Datos(consulta, pedido, ACCION_RESPUESTA);
            datos["conReembolso"] = consulta.ConReembolso == true ? "true" : "false";
            return new NotificacionPushDTO
            {
                Titulo = $"Pedido {consulta.Pedido}: {Decision(consulta)}",
                Cuerpo = $"Administración ({RetencionNombre(consulta.UsuarioRespuesta)}) dice: el pedido {consulta.Pedido}{TextoCliente(pedido)} {Decision(consulta)}.{enlaces}{comentario}{sigue}",
                Tipo = TIPO_NOTIFICACION,
                Datos = datos
            };
        }

        /// <summary>Con «sin reembolso», si el pedido ya tiene un envío en reparto con reembolso: eso no lo cambia la API (#725).</summary>
        public static string AvisoEnvioYaHecho(EnviosAgencia envio)
            => $"El pedido ya tiene el envío {envio.Numero} en reparto con un reembolso de {Importe(envio.Reembolso)}: cambie el reembolso en Agencias " +
               "(confirmándolo con la agencia), porque eso no se cambia solo.";

        private static string RetencionNombre(string usuario) => TextosRetencion.NombreUsuario(usuario);

        private static string TextoCliente(DatosPagoPedidoConsulta pedido)
        {
            if (pedido == null || string.IsNullOrWhiteSpace(pedido.Cliente))
            {
                return string.Empty;
            }
            string nombre = pedido.NombreCliente?.Trim();
            return string.IsNullOrEmpty(nombre) ? $" del cliente {pedido.Cliente.Trim()}" : $" del cliente {pedido.Cliente.Trim()} ({nombre})";
        }

        private static Dictionary<string, string> Datos(ConsultaReembolsoDTO consulta, DatosPagoPedidoConsulta pedido, string accion)
        {
            return new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["accion"] = accion,
                ["consulta"] = consulta.Id.ToString(CultureInfo.InvariantCulture),
                ["empresa"] = consulta.Empresa?.Trim(),
                ["pedido"] = consulta.Pedido.ToString(CultureInfo.InvariantCulture),
                ["cliente"] = pedido?.Cliente?.Trim()
            };
        }
    }
}
