using ModeloSugerenciasProductos.Entrenamiento;
using ModeloSugerenciasProductos.Features;
using ModeloSugerenciasProductos.Reglas;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Compra = ModeloSugerenciasProductos.Datos.Compra;
using FichaProducto = ModeloSugerenciasProductos.Datos.FichaProducto;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>
    /// NestoAPI#671 (corte 2c), sacado de ServicioSugerenciasProductos.PuntuarConModelo en NestoAPI#736: las entradas del
    /// modelo 1 (propensión) de un cliente, con las features calculadas EXACTAMENTE como en el entrenamiento
    /// (<see cref="CalculadoraFeatures"/> del núcleo, con «d» = hoy: solo compras anteriores a hoy y las tablas del job de
    /// anoche). Lo usan las sugerencias de la plantilla y la puerta del recordatorio de reposición
    /// (<see cref="PuntuadorReposicionModelo"/>): una sola preparación.
    /// <para>Lecturas: la popularidad de los candidatos (una consulta) y, si hace falta, las fichas de los productos del perfil
    /// que no se tenían (otra; se añaden a <c>fichas</c>) y el perfil sin las compras de hoy (casi nunca). Con
    /// <c>versionFeatures</c> 2 o más (NestoAPI#683), además el estado del cliente y la popularidad por estado.</para>
    /// </summary>
    internal static class EntradasModelo1
    {
        /// <param name="adicionales">Productos que también se quieren puntuar (ver <see cref="CalculadoraFeatures.Calcular"/>).</param>
        internal static async Task<ResultadoCandidatos> Calcular(IRepositorioSugerenciasProductos repositorio, int versionFeatures, DateTime hoy,
            string cliente, string empresa, List<CompraDiaReposicion> compras, HashSet<string> enCarrito, List<ParCompradoJunto> pares,
            List<AfinidadCliente> afinidades, Dictionary<string, FichaProductoSugerencia> fichas, Tuple<string, string> perfil,
            Dictionary<string, int> clientesPerfil, List<string> adicionales)
        {
            List<Compra> comprasNucleo = MotorSugerenciasProductos.ACompras(compras).ToList();
            Func<string, FichaProducto> ficha = MotorSugerenciasProductos.Buscador(fichas);

            // El perfil como en el entrenamiento (compras anteriores a d); casi siempre es el de las reglas
            Tuple<string, string> perfilModelo = ReglasSugerencias.Perfil(comprasNucleo.Where(c => c.Dia < hoy), ficha);
            Dictionary<string, int> clientesPerfilModelo = clientesPerfil;
            if (perfilModelo != null && (perfil == null || !string.Equals(perfil.Item1, perfilModelo.Item1, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(perfil.Item2, perfilModelo.Item2, StringComparison.OrdinalIgnoreCase)))
            {
                clientesPerfilModelo = await repositorio.LeerClientesPerfil(perfilModelo.Item1, perfilModelo.Item2).ConfigureAwait(false)
                    ?? new Dictionary<string, int>();
            }
            List<ProductoDePerfil> perfiles = perfilModelo == null ? new List<ProductoDePerfil>()
                : clientesPerfilModelo.Where(c => !string.IsNullOrWhiteSpace(c.Key)).Select(c => new ProductoDePerfil
                {
                    Grupo = perfilModelo.Item1 ?? string.Empty,
                    SubGrupo = perfilModelo.Item2 ?? string.Empty,
                    Producto = c.Key.Trim(),
                    Clientes = c.Value
                }).ToList();

            // Las fichas de los primeros del perfil (el modelo coge los 15 primeros que se pueden sugerir)
            List<string> sinFicha = perfiles.OrderByDescending(p => p.Clientes).ThenBy(p => p.Producto, StringComparer.OrdinalIgnoreCase)
                .Take(MotorSugerenciasProductos.FICHAS_PERFIL_MODELO).Select(p => p.Producto)
                .Where(p => !fichas.ContainsKey(p)).ToList();
            if (sinFicha.Count > 0)
            {
                Dictionary<string, FichaProductoSugerencia> nuevas = await repositorio.LeerFichas(empresa, sinFicha).ConfigureAwait(false);
                foreach (KeyValuePair<string, FichaProductoSugerencia> f in nuevas ?? new Dictionary<string, FichaProductoSugerencia>())
                {
                    fichas[f.Key.Trim()] = f.Value;
                }
            }

            List<ParComprado> paresNucleo = pares.Where(p => p != null).Select(MotorSugerenciasProductos.AParNucleo).ToList();
            List<AfinidadProducto> afinidadesNucleo = afinidades.Where(a => a?.Producto != null).Select(a => new AfinidadProducto
            {
                Cliente = (a.Cliente ?? cliente).Trim(),
                Producto = a.Producto.Trim(),
                Afinidad = a.Afinidad,
                Rango = a.Rango
            }).ToList();
            int maximoPorFuente = new OpcionesEntrenamiento().MaximoCandidatosPorFuente;

            // Dos pasadas: la primera da los candidatos (la popularidad no cambia cuáles son) y la segunda, con su popularidad
            ContextoInstantanea sinPopularidad = ContextoInstantanea.Desde(hoy, paresNucleo, afinidadesNucleo, null, perfiles);
            ResultadoCandidatos previos = CalculadoraFeatures.Calcular(sinPopularidad, cliente, comprasNucleo, enCarrito, ficha, hoy, maximoPorFuente, adicionales);
            List<PopularidadProducto> popularidad = await repositorio.LeerPopularidad(previos.Filas.Select(f => f.Producto)).ConfigureAwait(false)
                ?? new List<PopularidadProducto>();
            short? estadoCliente = null;
            List<PopularidadPorEstado> popularidadEstado = null;
            if (versionFeatures >= 2)
            {
                estadoCliente = await repositorio.LeerEstadoCliente(empresa, cliente).ConfigureAwait(false);
                popularidadEstado = await repositorio.LeerPopularidadEstado(EstadosCliente.Grupo(estadoCliente),
                    previos.Filas.Select(f => f.Producto), previos.Filas.Select(f => f.Familia).Where(f => !string.IsNullOrWhiteSpace(f))).ConfigureAwait(false);
            }
            ContextoInstantanea contexto = ContextoInstantanea.Desde(hoy, paresNucleo, afinidadesNucleo,
                popularidad.Where(p => p?.Producto != null).Select(p => new PopularidadDeProducto
                {
                    Producto = p.Producto.Trim(),
                    Clientes12Meses = p.Clientes12Meses,
                    Clientes3Meses = p.Clientes3Meses,
                    Cestas12Meses = p.Cestas12Meses,
                    Rango = p.Rango
                }), perfiles, popularidadEstado);
            ResultadoCandidatos features = CalculadoraFeatures.Calcular(contexto, cliente, comprasNucleo, enCarrito, ficha, hoy, maximoPorFuente, adicionales,
                estadoCliente);

            return features;
        }
    }
}
