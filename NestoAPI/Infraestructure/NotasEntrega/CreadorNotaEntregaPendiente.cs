using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>NestoAPI#542: interruptor de la nota de entrega automática (parámetro NotaEntregaAutomatica).</summary>
    public enum ModoNotaEntregaAutomatica
    {
        Apagado,
        /// <summary>Solo deja en ELMAH la nota que habría creado, para compararla con la que hace almacén a mano.</summary>
        Sombra,
        Encendido
    }

    /// <summary>
    /// NestoAPI#542: cuando un albarán lleva líneas con <c>Recoger</c> (el pedido se factura entero pero esas
    /// unidades no se entregan), prdCrearAlbaránVta ya ha hecho lo suyo (factura la cantidad completa, mueve
    /// solo el stock entregado y marca YaFacturado). Lo que faltaba, y hasta ahora hacía almacén a mano línea
    /// a línea desde el Nesto viejo (138 notas en 2026), es el pedido NOTA DE ENTREGA con lo pendiente: aquí se
    /// crea solo, con la cabecera del pedido original, el modo de servicio heredado y las líneas con
    /// Cantidad = Recoger y YaFacturado, que es lo que ServicioNotasEntrega procesa dando de baja el stock.
    ///
    /// <para>Se crea al hacer el ALBARÁN, no la factura (Carlos, 25/09/26): es cuando el SP marca YaFacturado
    /// y con periodo fin de mes la factura llega semanas después. Nunca rompe el albarán (ya está hecho): un
    /// fallo se registra en ELMAH y se sigue. Nunca se crea dos veces: (PedidoOrigen, AlbaranOrigen) la
    /// identifica. Y nunca es retroactiva: solo actúa sobre albaranes creados con el interruptor encendido.</para>
    /// </summary>
    public class CreadorNotaEntregaPendiente
    {
        internal const string VALOR_SOMBRA = "Sombra";
        internal const string VALOR_ENCENDIDO = "1";
        internal const string MARCA = "NestoAPI#542";

        private readonly NVEntities db;
        private readonly GestorPedidosVenta gestorPedidos;
        private readonly Action<Exception, string> registrar;

        public CreadorNotaEntregaPendiente(NVEntities db, GestorPedidosVenta gestorPedidos) : this(db, gestorPedidos, null)
        {
        }

        /// <summary>El registro (ELMAH) es sustituible en tests para comprobar la sombra.</summary>
        internal CreadorNotaEntregaPendiente(NVEntities db, GestorPedidosVenta gestorPedidos, Action<Exception, string> registrar)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.gestorPedidos = gestorPedidos ?? throw new ArgumentNullException(nameof(gestorPedidos));
            this.registrar = registrar ?? ElmahHelper.Log;
        }

        /// <summary>
        /// El gancho que llama ServicioAlbaranesVenta tras cada albarán. Con su propio contexto (el del
        /// que albaranea puede ir a medias de otra cosa) y sin lanzar nunca.
        /// </summary>
        public static async Task TrasAlbaran(string empresa, int pedido, int albaran, string usuario)
        {
            try
            {
                ModoNotaEntregaAutomatica modo = LeerModo(new LectorParametrosUsuario());
                if (modo == ModoNotaEntregaAutomatica.Apagado)
                {
                    return;
                }
                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    var creador = new CreadorNotaEntregaPendiente(db, new GestorPedidosVenta(new ServicioPedidosVenta()));
                    _ = await creador.Crear(empresa, pedido, albaran, usuario, modo).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception(
                    $"[Nota de entrega automática #542] No se ha podido crear la nota con lo pendiente del pedido {pedido} " +
                    $"(albarán {albaran}): {ex.Message}. El albarán está bien; la nota hay que hacerla a mano.", ex), usuario);
            }
        }

        /// <summary>Cualquier fallo al leer el parámetro cuenta como apagado.</summary>
        internal static ModoNotaEntregaAutomatica LeerModo(ILectorParametrosUsuario lector)
        {
            try
            {
                return InterpretarModo(lector.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, Constantes.ParametrosUsuario.NOTA_ENTREGA_AUTOMATICA));
            }
            catch
            {
                return ModoNotaEntregaAutomatica.Apagado;
            }
        }

        internal static ModoNotaEntregaAutomatica InterpretarModo(string valor)
        {
            string v = valor?.Trim();
            if (string.Equals(v, VALOR_SOMBRA, StringComparison.OrdinalIgnoreCase))
            {
                return ModoNotaEntregaAutomatica.Sombra;
            }
            return v == VALOR_ENCENDIDO ? ModoNotaEntregaAutomatica.Encendido : ModoNotaEntregaAutomatica.Apagado;
        }

        /// <summary>
        /// Crea la nota con lo pendiente del albarán, o devuelve null si no hay nada pendiente, ya existe, o el
        /// modo es sombra (entonces deja en ELMAH lo que habría creado).
        /// </summary>
        public async Task<CabPedidoVta> Crear(string empresa, int pedido, int albaran, string usuario, ModoNotaEntregaAutomatica modo)
        {
            if (modo == ModoNotaEntregaAutomatica.Apagado)
            {
                return null;
            }
            CabPedidoVta original = db.CabPedidoVtas.FirstOrDefault(c => c.Empresa == empresa && c.Número == pedido);
            if (original == null || original.NotaEntrega)
            {
                return null;
            }
            List<LinPedidoVta> pendientes = LineasPendientes(db.LinPedidoVtas.Where(l => l.Empresa == empresa && l.Número == pedido), albaran);
            if (!pendientes.Any())
            {
                return null;
            }
            if (db.CabPedidoVtas.Any(c => c.Empresa == empresa && c.PedidoOrigen == pedido && c.AlbaranOrigen == albaran))
            {
                return null;
            }
            if (modo == ModoNotaEntregaAutomatica.Sombra)
            {
                registrar(new Exception(Resumen("SOMBRA: habría creado", pedido, albaran, pendientes)), usuario);
                return null;
            }

            int numero = db.TomarSiguienteNumeroPedido();
            CabPedidoVta nota = ConstruirCabecera(original, numero, albaran, usuario, DateTime.Today);
            db.CabPedidoVtas.Add(nota);
            foreach (LinPedidoVta pendiente in pendientes)
            {
                LinPedidoVta linea = ConstruirLinea(pendiente, nota, usuario, DateTime.Today);
                gestorPedidos.CalcularImportesLinea(linea, nota.IVA);
                db.LinPedidoVtas.Add(linea);
            }
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
            return nota;
        }

        /// <summary>Las líneas de producto de ESE albarán que se facturaron con unidades sin entregar.</summary>
        internal static List<LinPedidoVta> LineasPendientes(IEnumerable<LinPedidoVta> lineasDelPedido, int albaran)
        {
            return lineasDelPedido
                .Where(l => l.Nº_Albarán == albaran && l.YaFacturado && l.Recoger != 0
                    && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                .OrderBy(l => l.Nº_Orden)
                .ToList();
        }

        /// <summary>
        /// La cabecera es la del pedido original (Carlos, 25/09/26: ruta, forma de pago, plazos y vendedor los
        /// del pedido, no los de la ficha), marcada como nota de entrega y con el modo de servicio heredado.
        /// </summary>
        internal static CabPedidoVta ConstruirCabecera(CabPedidoVta original, int numero, int albaran, string usuario, DateTime hoy)
        {
            CabPedidoVta nota = original.ClonarParaEmpresa(original.Empresa);
            nota.Número = numero;
            nota.Fecha = hoy;
            nota.Primer_Vencimiento = hoy;
            nota.NotaEntrega = true;
            // Una nota no se factura: sin modo de facturación y sin retener nada
            nota.MantenerJunto = false;
            nota.ModoFacturacion = null;
            byte modoHeredado = ModoServicioHeredado(original);
            nota.ModoServicio = modoHeredado;
            nota.ServirJunto = Constantes.Pedidos.ModosServicio.EsTodoJunto(modoHeredado);
            nota.PedidoOrigen = original.Número;
            nota.AlbaranOrigen = albaran;
            nota.Comentarios = ComentarioNota(original.Comentarios, original.Número, albaran);
            nota.AvisarConImporteAlCogerPicking = false; // no hay nada que cobrar al entregar
            nota.Agrupada = false;
            nota.FijarPrimerVto = false;
            nota.Usuario = usuario;
            return nota;
        }

        /// <summary>
        /// El modo de servicio del original, tal cual, salvo el 4 («ahora lo que hay y el resto de una vez»):
        /// la nota ES ese resto, así que va todo junto.
        /// </summary>
        internal static byte ModoServicioHeredado(CabPedidoVta original)
        {
            byte modo = Constantes.Pedidos.ModosServicio.Efectivo(original.ModoServicio, original.ServirJunto);
            return modo == Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ
                ? Constantes.Pedidos.ModosServicio.TODO_JUNTO
                : modo;
        }

        internal static string ComentarioNota(string comentarioOriginal, int pedido, int albaran)
        {
            string etiqueta = $"NOTA DE ENTREGA: pendiente de entregar del pedido {pedido} (albarán {albaran})";
            string original = comentarioOriginal?.Trim();
            return string.IsNullOrEmpty(original) ? etiqueta : original + "\r\n" + etiqueta;
        }

        /// <summary>
        /// La línea es la original con las unidades que se quedaron sin entregar, ya facturada (YaFacturado),
        /// limpia de albarán, factura y picking, y con sus importes recalculados para esa cantidad. Conserva la
        /// fecha de modificación del original: el cliente ya ha pagado esas unidades, y esa fecha es la
        /// antigüedad con la que el picking reparte el stock.
        /// </summary>
        internal static LinPedidoVta ConstruirLinea(LinPedidoVta original, CabPedidoVta nota, string usuario, DateTime hoy)
        {
            LinPedidoVta linea = original.ClonarParaEmpresa(nota.Empresa, nota.Número);
            linea.Nº_Orden = 0; // identidad: lo pone la BD
            linea.Cantidad = (short)original.Recoger;
            linea.Recoger = 0;
            linea.YaFacturado = true;
            linea.Estado = Constantes.EstadosLineaVenta.EN_CURSO;
            linea.Picking = 0;
            linea.Nº_Albarán = null;
            linea.Fecha_Albarán = null;
            linea.Nº_Factura = null;
            linea.Fecha_Factura = null;
            linea.Fecha_Entrega = hoy;
            linea.LineaParcial = false;
            linea.VtoBueno = true;
            linea.Usuario = usuario;
            linea.BlancoParaBorrar = MARCA;
            return linea;
        }

        private static string Resumen(string que, int pedido, int albaran, List<LinPedidoVta> pendientes)
        {
            string lineas = string.Join(", ", pendientes.Select(l => $"{l.Producto?.Trim()} × {l.Recoger}"));
            return $"[Nota de entrega automática #542] {que} la nota con lo pendiente del pedido {pedido} (albarán {albaran}): {lineas}.";
        }
    }
}
