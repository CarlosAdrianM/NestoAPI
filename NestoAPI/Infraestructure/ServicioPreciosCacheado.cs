using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#517: decorador de <see cref="IServicioPrecios"/> que memoiza las lecturas durante UNA
    /// petición (una validación de pedido, un cálculo de sugerencias). Los validadores preguntan por el
    /// mismo producto una y otra vez (ProductosMismoPrecio recorre todo el pedido por cada producto, y las
    /// sugerencias validan el pedido entero por cada candidata): el 23/09/26 eso fueron ~50.000 lecturas de
    /// Productos en 90 s y RDS2016 al 100 % de CPU. Aquí cada dato se lee una sola vez por petición.
    ///
    /// Vida corta a propósito: se crea por petición y se descarta. No es una caché entre peticiones
    /// (los precios, ofertas y stocks cambian y cada petición debe ver lo último).
    /// Las listas se devuelven como copia para que un llamante que las modifique no contamine al siguiente.
    /// FiltrarLineas y CalcularImporteGrupo dependen del pedido (que en las sugerencias es hipotético y
    /// cambia en cada candidata): pasan tal cual al servicio real.
    /// </summary>
    public class ServicioPreciosCacheado : IServicioPrecios
    {
        private readonly IServicioPrecios _servicio;
        private readonly ConcurrentDictionary<string, object> _cache = new ConcurrentDictionary<string, object>();

        public ServicioPreciosCacheado(IServicioPrecios servicio)
        {
            _servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
        }

        /// <summary>Si ya está envuelto no se vuelve a envolver (dos capas no ahorran nada).</summary>
        public static IServicioPrecios Envolver(IServicioPrecios servicio)
            => servicio is ServicioPreciosCacheado ? servicio : new ServicioPreciosCacheado(servicio);

        private T Memo<T>(string clave, Func<T> leer)
        {
            if (_cache.TryGetValue(clave, out object valor))
            {
                return (T)valor;
            }
            T leido = leer();
            _cache[clave] = leido;
            return leido;
        }

        private List<T> MemoLista<T>(string clave, Func<List<T>> leer)
        {
            List<T> lista = Memo(clave, leer);
            return lista == null ? null : new List<T>(lista);
        }

        private static string Normalizar(string valor) => valor?.Trim().ToUpperInvariant() ?? string.Empty;

        public Producto BuscarProducto(string producto)
            => Memo("P|" + Normalizar(producto), () => _servicio.BuscarProducto(producto));

        public List<OfertaPermitida> BuscarOfertasPermitidas(string producto)
            => MemoLista("OP|" + Normalizar(producto), () => _servicio.BuscarOfertasPermitidas(producto));

        public List<DescuentosProducto> BuscarDescuentosPermitidos(string numeroProducto, string numeroCliente, string contactoCliente)
            => MemoLista($"DP|{Normalizar(numeroProducto)}|{Normalizar(numeroCliente)}|{Normalizar(contactoCliente)}",
                () => _servicio.BuscarDescuentosPermitidos(numeroProducto, numeroCliente, contactoCliente));

        public List<OfertaCombinada> BuscarOfertasCombinadas(string numeroProducto)
            => MemoLista("OC|" + Normalizar(numeroProducto), () => _servicio.BuscarOfertasCombinadas(numeroProducto));

        public List<OfertaEscalonada> BuscarOfertasEscalonadas(string numeroProducto)
            => MemoLista("OE|" + Normalizar(numeroProducto), () => _servicio.BuscarOfertasEscalonadas(numeroProducto));

        public List<RegaloImportePedido> BuscarRegaloPorImportePedido(string numeroProducto)
            => MemoLista("RI|" + Normalizar(numeroProducto), () => _servicio.BuscarRegaloPorImportePedido(numeroProducto));

        public List<RegaloImportePedido> BuscarRegalosPorImportePedidoVigentes()
            => MemoLista("RIV", () => _servicio.BuscarRegalosPorImportePedidoVigentes());

        public int? BuscarGanavisionesProducto(string numeroProducto)
            => Memo("GV|" + Normalizar(numeroProducto), () => _servicio.BuscarGanavisionesProducto(numeroProducto));

        public int BuscarStockDisponibleParaRegalar(string numeroProducto, string almacen)
            => Memo($"ST|{Normalizar(numeroProducto)}|{Normalizar(almacen)}", () => _servicio.BuscarStockDisponibleParaRegalar(numeroProducto, almacen));

        public List<FamiliaIncompatibilidad> BuscarIncompatibilidadesFamilia(string familia)
            => MemoLista("FI|" + Normalizar(familia), () => _servicio.BuscarIncompatibilidadesFamilia(familia));

        public DateTime? UltimaCompraDeFamilia(string cliente, string familia, int meses)
            => Memo($"UC|{Normalizar(cliente)}|{Normalizar(familia)}|{meses}", () => _servicio.UltimaCompraDeFamilia(cliente, familia, meses));

        public decimal CalcularImporteGrupo(PedidoVentaDTO pedido, string grupo, string subGrupo)
            => _servicio.CalcularImporteGrupo(pedido, grupo, subGrupo);

        public List<LineaPedidoVentaDTO> FiltrarLineas(PedidoVentaDTO pedido, string filtroProducto, string familia)
            => _servicio.FiltrarLineas(pedido, filtroProducto, familia);

        public List<LineaPedidoVentaDTO> FiltrarLineas(PedidoVentaDTO pedido, string filtroProducto, string familia, string grupo, string subgrupo)
            => _servicio.FiltrarLineas(pedido, filtroProducto, familia, grupo, subgrupo);
    }
}
