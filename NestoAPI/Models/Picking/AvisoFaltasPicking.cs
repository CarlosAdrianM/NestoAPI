using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>Un producto que el mozo ha dado por «No está» en un pedido, con lo que hace falta para decidir.</summary>
    public class FaltaAvisoPicking
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Unidades { get; set; }
        /// <summary>Era una muestra (MMP) y se ha quitado del pedido en vez de quedar pendiente (Novedades 1228).</summary>
        public bool QuitadaDelPedido { get; set; }
        public bool EsMuestra { get; set; }
        /// <summary>Existencias en los OTROS almacenes (nombre del almacén → unidades), solo los que tienen.</summary>
        public List<KeyValuePair<string, int>> StockOtrosAlmacenes { get; set; } = new List<KeyValuePair<string, int>>();
        /// <summary>El primer pedido a proveedor enviado y sin recibir. Null = no hay compra pendiente.</summary>
        public CompraPendientePicking Compra { get; set; }
    }

    /// <summary>Un pedido de un picking terminado con faltas.</summary>
    public class PedidoConFaltasPicking
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int Picking { get; set; }
        /// <summary>«15191/0 - NOMBRE», ya montado.</summary>
        public string Cliente { get; set; }
        public string Almacen { get; set; }
        /// <summary>CabPedidoVta.MantenerJunto: el pedido se factura «al completar».</summary>
        public bool MantenerJunto { get; set; }
        /// <summary>Después de quitar las faltas no le queda ningún producto en el picking (sus portes esperan también).</summary>
        public bool NoSaleNada { get; set; }
        public List<FaltaAvisoPicking> Faltas { get; set; } = new List<FaltaAvisoPicking>();
    }

    /// <summary>A quién va el aviso de un pedido.</summary>
    public class DestinatariosAvisoFaltasPicking
    {
        /// <summary>Usuarios con dominio para el buzón (campana) de Nesto.</summary>
        public List<string> BuzonNesto { get; set; } = new List<string>();
        /// <summary>El usuario del pedido sin dominio, para su push de NestoApp (si tiene la app). Null si no se sabe.</summary>
        public string UsuarioNestoApp { get; set; }
        /// <summary>Los vendedores reales del pedido que NO son quien lo hizo: push de NestoApp (que también lo guarda en su buzón).</summary>
        public List<string> VendedoresNestoApp { get; set; } = new List<string>();
    }

    /// <summary>
    /// Novedades 1228 (picking 100070, 12/11/26): cuando un picking termina con productos dados por «No está», quien pasó el
    /// pedido no se enteraba de nada (veía la línea pendiente sin motivo). Ahora, por cada pedido afectado, un aviso corto y
    /// útil para decidir: qué falta, si queda pendiente o se ha quitado (muestra), si hay en otros almacenes, si hay compra
    /// pendiente y, si el pedido es «al completar», que no se factura mientras siga pendiente. Puro: sin base de datos.
    /// Los destinatarios son los del aviso de pendiente completo (#688): el buzón de quien hizo el pedido (y su push de
    /// NestoApp, como el aviso de picking con importe #555) y la push de su vendedor si es otra persona.
    /// </summary>
    public static class AvisoFaltasPicking
    {
        /// <summary>Tipo en la campana de Nesto y en la push de NestoApp (lleva «pedido»: NestoApp abre el pedido sin cambios).</summary>
        public const string TIPO_NOTIFICACION = "PickingConFaltas";
        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        internal static string Unidades(int unidades) => unidades == 1 ? "1 ud." : $"{unidades} uds.";

        private static string Producto(FaltaAvisoPicking falta)
        {
            string nombre = falta.Nombre?.Trim();
            return string.IsNullOrEmpty(nombre) ? falta.Producto?.Trim() : $"{falta.Producto?.Trim()} {nombre}";
        }

        private static string Destino(FaltaAvisoPicking falta) =>
            falta.QuitadaDelPedido ? "se ha quitado del pedido (muestra sin existencias)" : "queda pendiente";

        internal static string OtrosAlmacenes(FaltaAvisoPicking falta)
        {
            List<KeyValuePair<string, int>> con = (falta.StockOtrosAlmacenes ?? new List<KeyValuePair<string, int>>())
                .Where(s => s.Value > 0).ToList();
            return con.Any()
                ? "En otros almacenes: " + string.Join(", ", con.Select(s => $"{s.Key} {s.Value}")) + "."
                : "No hay en otros almacenes.";
        }

        internal static string Compra(FaltaAvisoPicking falta)
        {
            if (falta.Compra == null)
            {
                return "Sin compra pendiente.";
            }
            return falta.Compra.FechaPrevista.HasValue
                ? $"Próxima compra: {falta.Compra.FechaPrevista.Value.ToString("dd/MM", castellano)}."
                : $"Hay compra pendiente (pedido {falta.Compra.Pedido}) sin fecha prevista.";
        }

        /// <summary>Lo que se dice de cada falta después del «queda pendiente»: otros almacenes y, si sigue pendiente, la compra.</summary>
        private static string Detalle(FaltaAvisoPicking falta) =>
            falta.QuitadaDelPedido ? OtrosAlmacenes(falta) : $"{OtrosAlmacenes(falta)} {Compra(falta)}";

        private static string Almacen(PedidoConFaltasPicking pedido) =>
            string.IsNullOrWhiteSpace(pedido.Almacen) ? "el almacén" : "el almacén de " + StockLibreAgotadoPicking.NombreAlmacen(pedido.Almacen);

        /// <summary>El aviso de UN pedido para la campana y la push. Null si no tiene faltas.</summary>
        public static NotificacionPushDTO Componer(PedidoConFaltasPicking pedido)
        {
            List<FaltaAvisoPicking> faltas = pedido?.Faltas?.Where(f => f != null && f.Unidades > 0).ToList();
            if (faltas == null || faltas.Count == 0)
            {
                return null;
            }
            return new NotificacionPushDTO
            {
                Titulo = faltas.Count == 1
                    ? $"Pedido {pedido.Pedido}: no se ha encontrado {faltas[0].Producto?.Trim()} en el almacén"
                    : $"Pedido {pedido.Pedido}: no se han encontrado {faltas.Count} productos en el almacén",
                Cuerpo = Cuerpo(pedido, faltas),
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["empresa"] = pedido.Empresa?.Trim(),
                    ["pedido"] = pedido.Pedido.ToString(CultureInfo.InvariantCulture),
                    ["picking"] = pedido.Picking.ToString(CultureInfo.InvariantCulture)
                }
            };
        }

        internal static string Cuerpo(PedidoConFaltasPicking pedido, List<FaltaAvisoPicking> faltas)
        {
            string cabecera = $"Pedido {pedido.Pedido} (cliente {pedido.Cliente?.Trim()}): no se ha encontrado en {Almacen(pedido)}";
            string resto = pedido.NoSaleNada
                ? "No sale nada más de este pedido en este picking: espera con lo pendiente."
                : "El resto del pedido sigue su curso.";
            string texto;
            if (faltas.Count == 1)
            {
                FaltaAvisoPicking falta = faltas[0];
                texto = $"{cabecera} {Unidades(falta.Unidades)} de {Producto(falta)}; {Destino(falta)}. {resto} {Detalle(falta)}";
            }
            else
            {
                texto = cabecera + ":" + string.Concat(faltas.Select(f =>
                    $"{Environment.NewLine}- {Unidades(f.Unidades)} de {Producto(f)}: {Destino(f)}. {Detalle(f)}")) + Environment.NewLine + resto;
            }
            int pendientes = faltas.Count(f => !f.QuitadaDelPedido);
            if (pedido.MantenerJunto && pendientes > 0)
            {
                texto += pendientes == 1
                    ? " El pedido se sirve «al completar»: no se facturará mientras esa línea siga pendiente."
                    : " El pedido se sirve «al completar»: no se facturará mientras esas líneas sigan pendientes.";
            }
            return texto;
        }

        /// <summary>
        /// Quien hizo el pedido (buzón de Nesto y su push de NestoApp) y sus vendedores reales que sean OTRA persona (push de
        /// NestoApp). Si quien lo hizo es su vendedor, un solo aviso. Sin usuario del pedido, los usuarios de sus comerciales.
        /// </summary>
        public static DestinatariosAvisoFaltasPicking Destinatarios(ComercialesPedido comerciales)
        {
            var destinatarios = new DestinatariosAvisoFaltasPicking();
            if (comerciales == null)
            {
                return destinatarios;
            }
            destinatarios.BuzonNesto = AvisoPendienteCompleto.UsuariosBuzon(comerciales);
            destinatarios.UsuarioNestoApp = GestorAvisosPicking.UsuarioSinDominio(comerciales.UsuarioPedido);
            destinatarios.VendedoresNestoApp = AvisoPendienteCompleto.VendedoresAPush(comerciales);
            return destinatarios;
        }

        /// <summary>
        /// Lo que tiene que ver Compras: lo que queda pendiente, no es muestra y no tiene compra pendiente. Una línea por
        /// producto y pedido. Vacío si no hay nada.
        /// </summary>
        public static List<string> AvisosCompras(IEnumerable<PedidoConFaltasPicking> pedidos)
        {
            var avisos = new List<string>();
            foreach (PedidoConFaltasPicking pedido in (pedidos ?? Enumerable.Empty<PedidoConFaltasPicking>()).Where(p => p?.Faltas != null))
            {
                foreach (FaltaAvisoPicking falta in pedido.Faltas.Where(f => f != null && f.Unidades > 0 && !f.EsMuestra && !f.QuitadaDelPedido && f.Compra == null))
                {
                    avisos.Add($"Picking {pedido.Picking}, pedido {pedido.Pedido} (cliente {pedido.Cliente?.Trim()}): no se ha encontrado en {Almacen(pedido)} " +
                        $"{Unidades(falta.Unidades)} de {Producto(falta)} y no hay compra pendiente. {OtrosAlmacenes(falta)}");
                }
            }
            return avisos;
        }

        public static string TituloCompras(int picking) => $"Picking {picking}: faltas sin compra pendiente";
    }
}
