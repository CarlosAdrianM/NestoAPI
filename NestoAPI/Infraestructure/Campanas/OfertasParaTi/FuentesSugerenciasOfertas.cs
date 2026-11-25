using ModeloSugerenciasProductos.Reglas;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.SugerenciasProductos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>
    /// «Ofertas para ti» (Carlos 25/11/26: «lo más vendido de la campaña es muy genérico; con los mismos criterios con que sugerimos productos
    /// en la plantilla»): las fuentes de las sugerencias de la plantilla para un cliente, restringidas a los productos de la campaña.
    /// </summary>
    public interface IFuentesSugerenciasOfertas
    {
        /// <param name="compras">Sus compras de 24 meses (las que ya ha leído el núcleo).</param>
        /// <param name="productosCampana">Los productos de la campaña (las fuentes solo devuelven de estos).</param>
        Task<FuentesClienteOfertas> Leer(string empresa, string cliente, DateTime hoy, IReadOnlyCollection<CompraDiaReposicion> compras,
            ISet<string> productosCampana);
    }

    /// <summary>
    /// Con el repositorio de las sugerencias (sus mismas consultas, sin reimplementarlas): «comprado junto» con los pares del job nocturno
    /// (<see cref="CalculadoraParesCompradosJuntos"/>) tomando como «carrito» lo que ha comprado en 12 meses (<see cref="ReglasSugerencias.CompradoJunto"/>:
    /// por cada producto, el mejor par); «parecidos» con su afinidad item-kNN (<see cref="CalculadoraAfinidadProductos"/>,
    /// <see cref="MotorSugerenciasProductos.Parecidos"/>); y su perfil (<see cref="MotorSugerenciasProductos.Perfil"/>: lo que compran los clientes
    /// de su grupo y subgrupo, la base de «a potenciar»). Cuatro o cinco consultas por cliente (pares, fichas, afinidad, perfil).
    /// </summary>
    public class FuentesSugerenciasOfertasSql : IFuentesSugerenciasOfertas
    {
        /// <summary>Productos suyos que se usan como antecedentes de los pares (los de más días de compra en 12 meses).</summary>
        public const int MAXIMO_ANTECEDENTES = 200;

        private readonly IRepositorioSugerenciasProductos repositorio;

        public FuentesSugerenciasOfertasSql(IRepositorioSugerenciasProductos repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<FuentesClienteOfertas> Leer(string empresa, string cliente, DateTime hoy, IReadOnlyCollection<CompraDiaReposicion> compras,
            ISet<string> productosCampana)
        {
            var resultado = new FuentesClienteOfertas();
            ISet<string> campana = productosCampana ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<CompraDiaReposicion> suyas = (compras ?? new List<CompraDiaReposicion>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Producto) && c.Dia.Date < hoy.Date).ToList();
            var comprados = new HashSet<string>(suyas.Select(c => c.Producto.Trim()), StringComparer.OrdinalIgnoreCase);
            List<string> antecedentes = ReglaRankingOfertasParaTi.Antecedentes(suyas, hoy, MAXIMO_ANTECEDENTES);

            // Fichas de lo suyo: el perfil y los nombres de los antecedentes
            Dictionary<string, FichaProductoSugerencia> fichas = comprados.Count == 0
                ? new Dictionary<string, FichaProductoSugerencia>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, FichaProductoSugerencia>(await repositorio.LeerFichas(empresa, comprados.ToList()).ConfigureAwait(false)
                    ?? new Dictionary<string, FichaProductoSugerencia>(), StringComparer.OrdinalIgnoreCase);

            if (antecedentes.Count > 0)
            {
                List<ParCompradoJunto> pares = await repositorio.LeerPares(antecedentes).ConfigureAwait(false) ?? new List<ParCompradoJunto>();
                var carrito = new HashSet<string>(antecedentes, StringComparer.OrdinalIgnoreCase);
                foreach (ParComprado mejor in ReglasSugerencias.CompradoJunto(pares.Where(p => p?.ProductoB != null && campana.Contains(p.ProductoB.Trim()))
                    .Select(MotorSugerenciasProductos.AParNucleo), carrito))
                {
                    string a = mejor.ProductoA.Trim();
                    string nombre = fichas.TryGetValue(a, out FichaProductoSugerencia f) && !string.IsNullOrWhiteSpace(f.Nombre) ? f.Nombre.Trim() : a;
                    resultado.CompradoJunto[mejor.ProductoB.Trim()] = Tuple.Create(mejor.Confianza, nombre);
                }
            }

            List<AfinidadCliente> afinidades = await repositorio.LeerAfinidad(cliente).ConfigureAwait(false) ?? new List<AfinidadCliente>();
            foreach (CandidatoSugerencia c in MotorSugerenciasProductos.Parecidos(afinidades.Where(a => a?.Producto != null && campana.Contains(a.Producto.Trim())), comprados))
            {
                resultado.Parecidos[c.Producto] = c.Puntuacion;
            }

            Tuple<string, string> perfil = MotorSugerenciasProductos.Perfil(suyas, fichas);
            if (perfil != null)
            {
                Dictionary<string, int> clientesPerfil = await repositorio.LeerClientesPerfil(perfil.Item1, perfil.Item2).ConfigureAwait(false)
                    ?? new Dictionary<string, int>();
                foreach (KeyValuePair<string, int> p in clientesPerfil.Where(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value > 0
                    && campana.Contains(p.Key.Trim()) && !comprados.Contains(p.Key.Trim())))
                {
                    resultado.Perfil[p.Key.Trim()] = p.Value;
                }
            }
            return resultado;
        }
    }
}
