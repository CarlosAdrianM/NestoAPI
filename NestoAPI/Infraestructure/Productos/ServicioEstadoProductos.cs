using NestoAPI.Models;
using NestoAPI.Models.Productos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>La petición no vale (400): el mensaje va tal cual al usuario.</summary>
    public class PeticionEstadoProductosNoValidaException : Exception
    {
        public PeticionEstadoProductosNoValidaException(string mensaje) : base(mensaje)
        {
        }
    }

    /// <summary>NestoAPI#738 (sugerencia 793, Manuel): cambio de estado de los productos, uno a uno (ficha) y masivo.</summary>
    public interface IServicioEstadoProductos
    {
        Task<List<EstadoProductoDTO>> Estados(string empresa);

        Task<VistaPreviaEstadoProductosDTO> VistaPrevia(VistaPreviaEstadoProductosPeticion peticion, bool puedeCambiar);

        Task<ResultadoCambioEstadoProductosDTO> Cambiar(CambioEstadoProductosPeticion peticion, string usuario);
    }

    /// <summary>
    /// NestoAPI#738: orquesta el cambio de estado. Las reglas están en <see cref="ReglasEstadoProducto"/> (puras) y los datos en
    /// <see cref="IRepositorioEstadoProductos"/>. La vista previa y el cambio deciden con las mismas reglas y con los datos del
    /// momento (el cambio vuelve a leerlo todo: no se fía de lo que vio la pantalla).
    /// </summary>
    public class ServicioEstadoProductos : IServicioEstadoProductos
    {
        public const string MENSAJE_SIN_ESTADO = "Indique el estado nuevo.";
        public const string MENSAJE_SIN_FILTRO = "Indique un proveedor, una familia, un grupo, un subgrupo o una lista de productos.";
        public const string MENSAJE_SIN_PRODUCTOS = "Indique los productos a los que cambiar el estado.";

        private readonly IRepositorioEstadoProductos repositorio;

        public ServicioEstadoProductos(IRepositorioEstadoProductos repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public Task<List<EstadoProductoDTO>> Estados(string empresa) => repositorio.LeerEstados(Empresa(empresa));

        public async Task<VistaPreviaEstadoProductosDTO> VistaPrevia(VistaPreviaEstadoProductosPeticion peticion, bool puedeCambiar)
        {
            if (peticion == null)
            {
                throw new PeticionEstadoProductosNoValidaException(MENSAJE_SIN_FILTRO);
            }
            string empresa = Empresa(peticion.Empresa);
            short estadoNuevo = peticion.EstadoNuevo ?? throw new PeticionEstadoProductosNoValidaException(MENSAJE_SIN_ESTADO);
            FiltroEstadoProductos filtro = peticion.Filtro;
            bool hayFiltro = filtro != null && (!string.IsNullOrWhiteSpace(filtro.Proveedor) || !string.IsNullOrWhiteSpace(filtro.Familia)
                || !string.IsNullOrWhiteSpace(filtro.Grupo) || !string.IsNullOrWhiteSpace(filtro.Subgrupo)
                || (filtro.Productos != null && filtro.Productos.Any(p => !string.IsNullOrWhiteSpace(p))));
            if (!hayFiltro)
            {
                throw new PeticionEstadoProductosNoValidaException(MENSAJE_SIN_FILTRO);
            }
            EstadoProductoDTO infoNuevo = await ValidarEstado(empresa, estadoNuevo).ConfigureAwait(false);

            List<string> referencias = await repositorio.ProductosDelFiltro(empresa, filtro).ConfigureAwait(false);
            ValidarTamano(referencias.Count);
            Lote lote = await Decidir(empresa, referencias, estadoNuevo, infoNuevo, quitarControlesStock: true).ConfigureAwait(false);

            VistaPreviaEstadoProductosDTO vista = new VistaPreviaEstadoProductosDTO
            {
                PuedeCambiar = puedeCambiar,
                ControlesStockAfectados = lote.Decisiones.Count(d => d.AlmacenesConControles.Count > 0),
                ProductosConPedidosPendientes = lote.Decisiones.Count(d => d.TienePedidosPendientes),
                PreguntaControlesStock = ReglasEstadoProducto.PreguntaControlesStock(lote.Decisiones),
                Avisos = ReglasEstadoProducto.AvisosDelLote(lote.Decisiones)
            };
            foreach (DecisionEstadoProducto decision in lote.Decisiones)
            {
                lote.Datos.TryGetValue(decision.Producto, out DatosEstadoProducto d);
                vista.Productos.Add(new ProductoEstadoVistaPreviaDTO
                {
                    Producto = decision.Producto,
                    Nombre = d?.Nombre,
                    EstadoActual = d?.Estado,
                    EstadoNuevo = estadoNuevo,
                    StockALG = d?.StockEn(Constantes.Almacenes.ALGETE) ?? 0,
                    StockREI = d?.StockEn(Constantes.Almacenes.REINA) ?? 0,
                    StockALC = d?.StockEn(Constantes.Almacenes.ALCOBENDAS) ?? 0,
                    PendienteRecibirProveedor = d?.PendienteRecibir ?? 0,
                    PedidosPendientes = d?.PedidosPendientes ?? new List<PedidoPendienteEstadoDTO>(),
                    TieneControlesStock = new ControlesStockAlmacenesDTO
                    {
                        ALG = decision.AlmacenesConControles.Contains(Constantes.Almacenes.ALGETE),
                        REI = decision.AlmacenesConControles.Contains(Constantes.Almacenes.REINA),
                        ALC = decision.AlmacenesConControles.Contains(Constantes.Almacenes.ALCOBENDAS)
                    },
                    ControlesSugeridos = decision.ControlesACrear,
                    Avisos = decision.Avisos,
                    MotivoNoSeCambia = decision.Motivo
                });
            }
            return vista;
        }

        public async Task<ResultadoCambioEstadoProductosDTO> Cambiar(CambioEstadoProductosPeticion peticion, string usuario)
        {
            if (peticion == null)
            {
                throw new PeticionEstadoProductosNoValidaException(MENSAJE_SIN_PRODUCTOS);
            }
            string empresa = Empresa(peticion.Empresa);
            short estadoNuevo = peticion.EstadoNuevo ?? throw new PeticionEstadoProductosNoValidaException(MENSAJE_SIN_ESTADO);
            List<string> referencias = RepositorioEstadoProductos.Limpiar(peticion.Productos);
            if (referencias.Count == 0)
            {
                throw new PeticionEstadoProductosNoValidaException(MENSAJE_SIN_PRODUCTOS);
            }
            ValidarTamano(referencias.Count);
            EstadoProductoDTO infoNuevo = await ValidarEstado(empresa, estadoNuevo).ConfigureAwait(false);

            Lote lote = await Decidir(empresa, referencias, estadoNuevo, infoNuevo, peticion.QuitarControlesStock).ConfigureAwait(false);
            ResultadoCambioEstadoProductosDTO resultado = new ResultadoCambioEstadoProductosDTO
            {
                Omitidos = lote.Decisiones.Where(d => d.Omitir).Select(d => new ProductoOmitidoDTO { Producto = d.Producto, Motivo = d.Motivo }).ToList()
            };
            List<DecisionEstadoProducto> aplicar = lote.Decisiones.Where(d => !d.Omitir).ToList();
            if (aplicar.Count > 0)
            {
                ResultadoAplicacionEstado aplicado = await repositorio.Aplicar(empresa, aplicar, usuario).ConfigureAwait(false);
                resultado.Cambiados = aplicado.Cambiados;
                resultado.ControlesStockQuitados = aplicado.ControlesQuitados;
                resultado.ControlesStockCreados = aplicado.ControlesCreados;
            }
            int sinControles = aplicar.Count(d => d.VuelveADisponibleSinControles);
            if (sinControles > 0)
            {
                resultado.Avisos.Add(sinControles == 1
                    ? "1 producto ha vuelto a Disponible sin controles de stock: revise los máximos."
                    : $"{sinControles} productos han vuelto a Disponible sin controles de stock: revise los máximos.");
            }
            int conControles = resultado.Omitidos.Count(o => o.Motivo != null && o.Motivo.StartsWith("Tiene controles de stock", StringComparison.Ordinal));
            if (conControles > 0)
            {
                resultado.Avisos.Add(conControles == 1
                    ? "1 producto no ha cambiado de estado porque tiene controles de stock."
                    : $"{conControles} productos no han cambiado de estado porque tienen controles de stock.");
            }
            return resultado;
        }

        private async Task<EstadoProductoDTO> ValidarEstado(string empresa, short estadoNuevo)
        {
            List<EstadoProductoDTO> estados = await repositorio.LeerEstados(empresa).ConfigureAwait(false);
            if (!ReglasEstadoProducto.EsEstadoValido(estadoNuevo, estados))
            {
                throw new PeticionEstadoProductosNoValidaException(ReglasEstadoProducto.MensajeEstadoNoValido(estadoNuevo));
            }
            return estados.First(e => e.Estado == estadoNuevo);
        }

        private static void ValidarTamano(int cuantos)
        {
            if (cuantos > ReglasEstadoProducto.MAXIMO_PRODUCTOS_POR_LOTE)
            {
                throw new PeticionEstadoProductosNoValidaException(
                    string.Format(System.Globalization.CultureInfo.GetCultureInfo("es-ES"), "Son {0:N0} productos: como mucho {1:N0} por lote. Afine el filtro.",
                        cuantos, ReglasEstadoProducto.MAXIMO_PRODUCTOS_POR_LOTE));
            }
        }

        private async Task<Lote> Decidir(string empresa, List<string> referencias, short estadoNuevo, EstadoProductoDTO infoNuevo, bool quitarControlesStock)
        {
            Dictionary<string, DatosEstadoProducto> datos = referencias.Count == 0
                ? new Dictionary<string, DatosEstadoProducto>(StringComparer.OrdinalIgnoreCase)
                : await repositorio.Cargar(empresa, referencias).ConfigureAwait(false);
            List<string> vuelven = referencias
                .Where(r => datos.TryGetValue(r, out DatosEstadoProducto d) && ReglasEstadoProducto.VuelveADisponible(d.Estado, estadoNuevo))
                .ToList();
            Dictionary<string, List<ControlStockSugeridoDTO>> sugeridos = vuelven.Count == 0
                ? new Dictionary<string, List<ControlStockSugeridoDTO>>(StringComparer.OrdinalIgnoreCase)
                : await repositorio.SugerirControles(empresa, vuelven).ConfigureAwait(false);

            List<DecisionEstadoProducto> decisiones = referencias.Select(r =>
            {
                datos.TryGetValue(r, out DatosEstadoProducto d);
                sugeridos.TryGetValue(r, out List<ControlStockSugeridoDTO> s);
                return ReglasEstadoProducto.Decidir(r, d, estadoNuevo, infoNuevo, quitarControlesStock, s);
            }).ToList();
            return new Lote { Datos = datos, Decisiones = decisiones };
        }

        private static string Empresa(string empresa) => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        private class Lote
        {
            public Dictionary<string, DatosEstadoProducto> Datos { get; set; }
            public List<DecisionEstadoProducto> Decisiones { get; set; }
        }
    }
}
