using ModeloSugerenciasProductos.Features;
using NestoAPI.Infraestructure.SugerenciasProductos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#736: el de verdad. Usa el modelo 1 de las sugerencias (<see cref="PredictorSugerenciasProductos"/>: el último zip
    /// que pasa la puerta, su interruptor ModeloSugerenciasActivo y su caché) y prepara las entradas con la MISMA función que
    /// las sugerencias de la plantilla (<see cref="EntradasModelo1"/>), con el carrito vacío: el recordatorio llega sin
    /// carrito, así que «comprado junto» no aporta y se mira como en una plantilla recién abierta.
    /// <para>Lecturas por cliente (las del repositorio de sugerencias): compras de 24 meses, afinidad, fichas, clientes del
    /// perfil y popularidad (y, con features v2, estado y popularidad por estado). Unos cientos de clientes a la semana.</para>
    /// <para>Si algo falla NO se llama a <see cref="IPredictorSugerenciasProductos.AvisarFallo"/>: eso dejaría a las
    /// sugerencias de la plantilla sin modelo por un fallo de esta lectura. La puerta lo trata como modelo no disponible.</para>
    /// </summary>
    public class PuntuadorReposicionModelo : IPuntuadorReposicion
    {
        private readonly IRepositorioSugerenciasProductos repositorio;
        private readonly IPredictorSugerenciasProductos predictor;

        public PuntuadorReposicionModelo(IRepositorioSugerenciasProductos repositorio, IPredictorSugerenciasProductos predictor)
        {
            this.repositorio = repositorio;
            this.predictor = predictor;
        }

        public string ModeloDisponible() => repositorio == null ? null : predictor?.ModeloDisponible();

        public async Task<Dictionary<string, float>> Puntuar(string empresa, string cliente, DateTime hoy, IReadOnlyCollection<string> productos)
        {
            var resultado = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var buscados = new HashSet<string>((productos ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()),
                StringComparer.OrdinalIgnoreCase);
            if (buscados.Count == 0 || string.IsNullOrWhiteSpace(cliente))
            {
                return resultado;
            }
            DateTime dia = hoy.Date;
            cliente = cliente.Trim();

            List<CompraDiaReposicion> compras = await repositorio.LeerComprasCliente(empresa, cliente,
                dia.AddMonths(-MotorSugerenciasProductos.MESES_HISTORIAL), dia.AddDays(1)).ConfigureAwait(false) ?? new List<CompraDiaReposicion>();
            List<AfinidadCliente> afinidades = await repositorio.LeerAfinidad(cliente).ConfigureAwait(false) ?? new List<AfinidadCliente>();
            List<string> conFicha = compras.Select(c => c.Producto)
                .Concat(afinidades.Select(a => a?.Producto))
                .Concat(buscados)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var fichas = new Dictionary<string, FichaProductoSugerencia>(
                await repositorio.LeerFichas(empresa, conFicha).ConfigureAwait(false) ?? new Dictionary<string, FichaProductoSugerencia>(),
                StringComparer.OrdinalIgnoreCase);
            Tuple<string, string> perfil = MotorSugerenciasProductos.Perfil(compras, fichas);
            var clientesPerfil = new Dictionary<string, int>(perfil == null ? new Dictionary<string, int>()
                : await repositorio.LeerClientesPerfil(perfil.Item1, perfil.Item2).ConfigureAwait(false) ?? new Dictionary<string, int>(),
                StringComparer.OrdinalIgnoreCase);

            ResultadoCandidatos features = await EntradasModelo1.Calcular(repositorio, predictor.VersionFeatures, dia, cliente, empresa, compras,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), new List<ParCompradoJunto>(), afinidades, fichas, perfil, clientesPerfil,
                buscados.ToList()).ConfigureAwait(false);
            List<ModeloSugerenciaEntrada> entradas = features.Filas.Cast<ModeloSugerenciaEntrada>().ToList();
            if (entradas.Count == 0)
            {
                return resultado;
            }
            IList<float> probabilidades = predictor.Puntuar(entradas);
            if (probabilidades == null || probabilidades.Count != entradas.Count)
            {
                throw new InvalidOperationException($"El modelo ha devuelto {probabilidades?.Count ?? 0} probabilidades para {entradas.Count} candidatos.");
            }
            for (int i = 0; i < features.Filas.Count; i++)
            {
                string producto = features.Filas[i].Producto?.Trim();
                if (producto != null && buscados.Contains(producto))
                {
                    resultado[producto] = probabilidades[i];
                }
            }
            return resultado;
        }
    }
}
