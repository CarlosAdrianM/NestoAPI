using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Models.Picking
{
    /// <summary>Una muestra (o parte de ella) que el picking ha quitado del pedido por no tener stock.</summary>
    public class MuestraQuitadaPicking
    {
        /// <summary>Nº Orden de la línea en LinPedidoVta.</summary>
        public int IdLinea { get; set; }
        public string Producto { get; set; }
        /// <summary>Unidades que se quitan del pedido (las que no tenían stock).</summary>
        public int UnidadesQuitadas { get; set; }
        /// <summary>Unidades que sí salen en este picking. 0 = la línea se borra entera.</summary>
        public int UnidadesQueSalen { get; set; }
    }

    /// <summary>
    /// 27/10/26 (Carlos, caso David): un error de stock en una MUESTRA (subgrupo MMP, 100 % de descuento) no puede retener
    /// un pedido «Todo junto» ni dejar la muestra pendiente en ningún modo. Si lo demás del pedido sale, las unidades de
    /// muestra sin stock se QUITAN del pedido (la línea se borra, o se reduce a lo que sí hay) y el pedido sale.
    ///
    /// <para>Generaliza NestoAPI#529 para las muestras: hasta ahora una muestra sin stock era un «regalo» más (base 0) y el
    /// pedido se quedaba esperándola (EsperaSoloRegalos) o, en «Todo junto», la retenía. Los demás regalos (Ganavisiones,
    /// regalo por importe, unidades gratis de una oferta: base 0 que NO son MMP) siguen igual: se identifica por SubGrupo, no
    /// por la base, para no confundirlos.</para>
    ///
    /// <para>Solo se quitan si al quitarlas el pedido SALE: si lo que falta es también algo de pago (o un regalo que no es
    /// muestra), el pedido se queda como antes, con la muestra dentro, y se decidirá en la siguiente pasada. Un pedido que solo
    /// lleva muestras tampoco se toca: no hay «resto» que servir. Las notas de entrega no entran.</para>
    ///
    /// <para>Va detrás del interruptor <see cref="CLAVE"/> del Web.config (true por defecto). Cada recorte queda en
    /// Modificaciones (usuario <see cref="USUARIO_AUDITORIA"/>), en ELMAH y en la campana de Nesto / push de NestoApp del
    /// usuario del pedido (<see cref="EscritorMuestrasSinStock"/> y <see cref="Avisar"/>).</para>
    /// </summary>
    public static class GestorMuestrasSinStock
    {
        public const string CLAVE = "Picking:MuestrasSinStockSeQuitan";
        public const string USUARIO_AUDITORIA = "Picking (muestras sin stock)";
        /// <summary>Tipo de la notificación en la campana de Nesto y en la push de NestoApp.</summary>
        public const string TIPO_NOTIFICACION = "MuestrasSinStockQuitadas";
        private const string DOMINIO = "NUEVAVISION\\";

        /// <summary>Para los tests (null = el Web.config).</summary>
        internal static Func<string, string> LeerAjuste { get; set; }

        /// <summary>El interruptor del Web.config. Sin la clave, encendido.</summary>
        public static bool Activo => Interpretar(Leer(CLAVE));

        internal static bool Interpretar(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                return true;
            }
            string limpio = valor.Trim();
            return !limpio.Equals("false", StringComparison.OrdinalIgnoreCase) && limpio != "0";
        }

        private static string Leer(string clave)
        {
            return LeerAjuste != null ? LeerAjuste(clave) : ConfigurationManager.AppSettings[clave];
        }

        /// <summary>
        /// Quita de cada candidato las unidades de muestra sin stock cuando, sin ellas, el pedido sale.
        /// <paramref name="saleSinLasMuestras"/> es la decisión de siempre (GestorPicking.Decidir), aplicada al pedido ya sin
        /// las muestras; si dice que no, el pedido se deja exactamente como estaba.
        /// </summary>
        public static void Aplicar(List<PedidoPicking> candidatos, Func<PedidoPicking, bool> saleSinLasMuestras)
        {
            if (!Activo || candidatos == null || saleSinLasMuestras == null)
            {
                return;
            }
            foreach (PedidoPicking pedido in candidatos)
            {
                _ = Aplicar(pedido, saleSinLasMuestras);
            }
        }

        /// <summary>Lo mismo para UN pedido; devuelve lo que se ha quitado (vacío si nada).</summary>
        internal static List<MuestraQuitadaPicking> Aplicar(PedidoPicking pedido, Func<PedidoPicking, bool> saleSinLasMuestras)
        {
            List<MuestraQuitadaPicking> nada = new List<MuestraQuitadaPicking>();
            if (!SeAplica(pedido))
            {
                return nada;
            }
            List<LineaPedidoPicking> muestras = MuestrasSinStock(pedido);

            // Se prueba a quitarlas y se pregunta si así sale; si no, se restaura todo tal cual estaba
            List<LineaPedidoPicking> lineasAntes = pedido.Lineas.ToList();
            Dictionary<LineaPedidoPicking, int> cantidadesAntes = muestras.ToDictionary(l => l, l => l.Cantidad);
            foreach (LineaPedidoPicking muestra in muestras)
            {
                if (muestra.CantidadReservada <= 0)
                {
                    _ = pedido.Lineas.Remove(muestra);
                }
                else
                {
                    muestra.Cantidad = muestra.CantidadReservada;
                }
            }

            bool sale;
            try
            {
                sale = saleSinLasMuestras(pedido);
            }
            finally
            {
                // La decisión de verdad la toma después el bucle de GestorPicking; aquí solo se ha ensayado
                pedido.MotivoNoSale = MotivoNoSalePicking.Ninguno;
                pedido.LineasQueFaltanAlDecidir = null;
            }

            if (!sale)
            {
                pedido.Lineas.Clear();
                pedido.Lineas.AddRange(lineasAntes);
                foreach (KeyValuePair<LineaPedidoPicking, int> cantidad in cantidadesAntes)
                {
                    cantidad.Key.Cantidad = cantidad.Value;
                }
                return nada;
            }

            List<MuestraQuitadaPicking> quitadas = muestras.Select(m => new MuestraQuitadaPicking
            {
                IdLinea = m.Id,
                Producto = m.Producto?.Trim(),
                UnidadesQuitadas = cantidadesAntes[m] - Math.Max(m.CantidadReservada, 0),
                UnidadesQueSalen = Math.Max(m.CantidadReservada, 0)
            }).ToList();
            pedido.MuestrasQuitadas.AddRange(quitadas);
            return quitadas;
        }

        /// <summary>
        /// Pedido normal (no nota de entrega, no retirado), con alguna muestra sin todo su stock y con algo MÁS que muestras
        /// que servir (si solo lleva muestras, no hay resto que sacar y se queda como antes).
        /// </summary>
        internal static bool SeAplica(PedidoPicking pedido)
        {
            if (pedido == null || pedido.EsNotaEntrega || pedido.Borrar || pedido.Lineas == null || pedido.Lineas.Count == 0)
            {
                return false;
            }
            if (!MuestrasSinStock(pedido).Any())
            {
                return false;
            }
            return pedido.Lineas.Any(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !l.EsMuestra && l.Cantidad > 0);
        }

        /// <summary>Las líneas de muestra (MMP) a las que falta stock en esta pasada.</summary>
        internal static List<LineaPedidoPicking> MuestrasSinStock(PedidoPicking pedido)
        {
            if (pedido?.Lineas == null)
            {
                return new List<LineaPedidoPicking>();
            }
            return pedido.Lineas
                .Where(l => l.EsMuestra && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && l.Cantidad > 0 && l.Cantidad > l.CantidadReservada)
                .ToList();
        }

        /// <summary>«44729 (1 ud), 43767 (2 de 3 uds)»: lo quitado de un pedido, para los avisos y el rastro.</summary>
        internal static string Describir(IEnumerable<MuestraQuitadaPicking> quitadas)
        {
            return string.Join(", ", (quitadas ?? Enumerable.Empty<MuestraQuitadaPicking>()).Select(q =>
            {
                string unidades = q.UnidadesQuitadas == 1 ? "1 ud" : $"{q.UnidadesQuitadas} uds";
                return q.UnidadesQueSalen > 0
                    ? $"{q.Producto} ({unidades} de {q.UnidadesQuitadas + q.UnidadesQueSalen})"
                    : $"{q.Producto} ({unidades})";
            }));
        }

        /// <summary>
        /// Campana de Nesto (buzón + SignalR) y push de NestoApp al usuario del pedido, como el aviso de picking con importe
        /// (NestoAPI#555), para que el vendedor sepa que la muestra no va y pueda decírselo al cliente. Nunca lanza.
        /// </summary>
        public static async Task Avisar(IEnumerable<PedidoPicking> pedidos, IServicioNotificacionesPush notificaciones)
        {
            if (pedidos == null || notificaciones == null)
            {
                return;
            }
            foreach (PedidoPicking pedido in pedidos.Where(p => p != null && p.MuestrasQuitadas != null && p.MuestrasQuitadas.Any()))
            {
                try
                {
                    string usuario = GestorAvisosPicking.UsuarioSinDominio(pedido.Usuario);
                    NotificacionPushDTO notificacion = ComponerNotificacion(pedido);
                    if (usuario == null || notificacion == null)
                    {
                        continue;
                    }
                    await notificaciones.GuardarEnBuzonDeUsuario(DOMINIO + usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                    _ = await notificaciones.EnviarAUsuario(usuario, Constantes.Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception(
                        $"[Picking muestras] No se pudo avisar en Nesto/NestoApp de las muestras quitadas del pedido {pedido.Empresa?.Trim()}/{pedido.Id}: {ex.Message}", ex),
                        USUARIO_AUDITORIA);
                }
            }
        }

        /// <summary>El aviso para la campana y la push. Null si no se ha quitado nada.</summary>
        internal static NotificacionPushDTO ComponerNotificacion(PedidoPicking pedido)
        {
            if (pedido?.MuestrasQuitadas == null || !pedido.MuestrasQuitadas.Any())
            {
                return null;
            }
            bool varias = pedido.MuestrasQuitadas.Count > 1 || pedido.MuestrasQuitadas.Any(q => q.UnidadesQuitadas > 1);
            return new NotificacionPushDTO
            {
                Titulo = $"Pedido {pedido.Id}: {(varias ? "muestras quitadas" : "muestra quitada")} por falta de stock",
                Cuerpo = $"Cliente {pedido.Cliente?.Trim()}: no había stock de {Describir(pedido.MuestrasQuitadas)}. " +
                    $"{(varias ? "Se han quitado del pedido" : "Se ha quitado del pedido")} y el resto ha cogido picking. Si lo considera oportuno, avise al cliente.",
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["empresa"] = pedido.Empresa?.Trim(),
                    ["pedido"] = pedido.Id.ToString()
                }
            };
        }
    }
}
