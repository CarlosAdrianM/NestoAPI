using NestoAPI.Infraestructure.Productos;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>NestoAPI#884: una muestra activa (subgrupo MMP) con lo que hace falta para darle código, etiquetar su contenedor y la hoja.</summary>
    public class MuestraAlmacen
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Productos.Familia (el código).</summary>
        public string Familia { get; set; }
        /// <summary>Familias.Descripción (la marca, «Lisap»).</summary>
        public string NombreFamilia { get; set; }
        public string SubGrupo { get; set; }
        public short? Estado { get; set; }
        /// <summary>Stock en el almacén pedido (ExtractoProducto de las empresas 1 y 3); 0 sin almacén.</summary>
        public int Stock { get; set; }
        /// <summary>Los códigos activos (los de ProductosCodigosBarras y el de la ficha, Principal).</summary>
        public List<CodigoBarrasProductoDTO> Codigos { get; set; } = new List<CodigoBarrasProductoDTO>();
        /// <summary>Los huecos con existencias en el almacén pedido (solo Algete tiene).</summary>
        public List<HuecoMuestra> Huecos { get; set; } = new List<HuecoMuestra>();
    }

    public class HuecoMuestra
    {
        public string Producto { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int Cantidad { get; set; }
    }

    public interface IRepositorioMuestras
    {
        /// <summary>
        /// Las muestras activas (MMP, Estado &gt;= 0) de la empresa con sus códigos activos y, si viene <paramref name="almacen"/>, su
        /// stock y sus huecos en él. Tres consultas, sin ir producto a producto.
        /// </summary>
        Task<List<MuestraAlmacen>> LeerMuestras(string empresa, string almacen);

        /// <summary>Una fila en Modificaciones (Usuario explícito: en el EDMX es Computed).</summary>
        Task ApuntarRastro(string tabla, string anterior, string nuevo, string usuario);
    }

    /// <summary>
    /// NestoAPI#884 (Carlos 24/11/26): las reglas de las muestras sin código de fabricante. Su etiqueta NO va en la unidad: va una por
    /// referencia en el cajón del mostrador (REI, ALC) o en la gaveta / el hueco (ALG), con el EAN-13 interno del 29 (#762) o, si lo
    /// tiene, el del fabricante. Puras: sin base de datos.
    /// </summary>
    public static class ReglasMuestras
    {
        public const int MAXIMO_ETIQUETAS = ServicioEtiquetasHueco.MAXIMO_ETIQUETAS;

        public static readonly IReadOnlyList<string> ALMACENES = new[]
        {
            Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS
        };

        public static string MensajeAlmacen => $"El almacén tiene que ser {string.Join(", ", ALMACENES.Take(ALMACENES.Count - 1))} o {ALMACENES.Last()}.";

        /// <summary>ALG, REI o ALC (sin mirar mayúsculas ni espacios); null si es otro o viene en blanco.</summary>
        public static string NormalizarAlmacen(string almacen)
        {
            string limpio = almacen?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(limpio) || !ALMACENES.Contains(limpio) ? null : limpio;
        }

        public static bool EsActiva(MuestraAlmacen muestra)
            => muestra != null && MuestrasSinCodigo.EsMuestra(muestra.SubGrupo) && muestra.Estado.HasValue && muestra.Estado.Value >= 0
                && !string.IsNullOrWhiteSpace(muestra.Producto);

        /// <summary>
        /// Tiene algún código de barras: un GTIN válido activo en la ficha o en ProductosCodigosBarras, el interno del 29 incluido
        /// (la misma regla que Ariadna al leer, <see cref="CompletadorCodigosBarras.Juntar"/>; uno que no es GTIN no cuenta, #674).
        /// </summary>
        public static bool TieneCodigo(MuestraAlmacen muestra)
            => (muestra?.Codigos ?? new List<CodigoBarrasProductoDTO>()).Any(c => c != null && c.Activo && ValidadorGtin.EsCodigoValido(c.Codigo?.Trim()));

        /// <summary>(a) Entra en el bloque del código interno: muestra activa sin ningún código. Idempotente: con el interno ya no entra.</summary>
        public static bool EntraEnBloque(MuestraAlmacen muestra) => EsActiva(muestra) && !TieneCodigo(muestra);

        /// <summary>(a) Las del bloque, por referencia.</summary>
        public static List<MuestraAlmacen> ParaElBloque(IEnumerable<MuestraAlmacen> muestras)
            => (muestras ?? Enumerable.Empty<MuestraAlmacen>()).Where(EntraEnBloque)
                .GroupBy(m => m.Producto.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(m => m.Producto.Trim(), StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>La familia pedida: su código (Productos.Familia) o su nombre, sin mirar mayúsculas ni espacios. Sin familia, todas.</summary>
        public static bool CasaFamilia(MuestraAlmacen muestra, string familia)
        {
            string buscada = familia?.Trim();
            return string.IsNullOrEmpty(buscada)
                || string.Equals(muestra?.Familia?.Trim(), buscada, StringComparison.OrdinalIgnoreCase)
                || string.Equals(muestra?.NombreFamilia?.Trim(), buscada, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>(b) y (d): las muestras activas de la familia, con stock si se pide y, si viene la lista, solo esas referencias.</summary>
        public static List<MuestraAlmacen> Filtrar(IEnumerable<MuestraAlmacen> muestras, string familia, bool soloConStock, IEnumerable<string> productos)
        {
            HashSet<string> pedidos = Pedidos(productos);
            return (muestras ?? Enumerable.Empty<MuestraAlmacen>())
                .Where(EsActiva)
                .Where(m => CasaFamilia(m, familia))
                .Where(m => !soloConStock || m.Stock > 0)
                .Where(m => pedidos.Count == 0 || pedidos.Contains(m.Producto.Trim()))
                .GroupBy(m => m.Producto.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .ToList();
        }

        /// <summary>(b) De las referencias pedidas, las que no son muestras activas.</summary>
        public static List<string> NoSonMuestras(IEnumerable<MuestraAlmacen> muestras, IEnumerable<string> productos)
        {
            var activas = new HashSet<string>((muestras ?? Enumerable.Empty<MuestraAlmacen>()).Where(EsActiva).Select(m => m.Producto.Trim()),
                StringComparer.OrdinalIgnoreCase);
            return Pedidos(productos).Where(p => !activas.Contains(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// (b) Las etiquetas en el orden en que se pegan: una por hueco con existencias en el orden del recorrido (Algete; la regla
        /// única <see cref="OrdenRecorrido"/>) y, detrás, una por cada muestra sin hueco (las tiendas, todas) por familia y nombre,
        /// las de sin familia al final. El código es el de la unidad (<see cref="ServicioEtiquetasProducto.CodigoDeLaUnidad"/>):
        /// el del fabricante si tiene; si no, el interno; null si aún no tiene ninguno.
        /// </summary>
        public static List<EtiquetaMuestraDTO> Etiquetas(IEnumerable<MuestraAlmacen> muestras, OrdenRecorrido orden)
        {
            OrdenRecorrido regla = orden ?? OrdenRecorrido.ComoHoy;
            StringComparer texto = StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), ignoreCase: true);
            var conHueco = new List<(MuestraAlmacen Muestra, HuecoMuestra Hueco)>();
            var sinHueco = new List<MuestraAlmacen>();
            foreach (MuestraAlmacen muestra in muestras ?? Enumerable.Empty<MuestraAlmacen>())
            {
                List<HuecoMuestra> huecos = (muestra.Huecos ?? new List<HuecoMuestra>())
                    .Where(h => h != null && h.Cantidad > 0 && !string.IsNullOrWhiteSpace(h.Pasillo))
                    .GroupBy(h => CasadorEscaneos.TextoUbicacion(h.Pasillo, h.Fila, h.Columna), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                if (huecos.Count == 0)
                {
                    sinHueco.Add(muestra);
                }
                else
                {
                    conHueco.AddRange(huecos.Select(h => (muestra, h)));
                }
            }
            IEnumerable<(MuestraAlmacen Muestra, HuecoMuestra Hueco)> ordenadas = regla
                .Ordenar(conHueco, x => x.Hueco.Pasillo, x => x.Hueco.Columna, x => x.Hueco.Fila)
                .ThenBy(x => x.Muestra.Producto?.Trim(), StringComparer.Ordinal)
                .Concat(sinHueco
                    .OrderBy(m => string.IsNullOrWhiteSpace(NombreFamilia(m)) ? 1 : 0)
                    .ThenBy(m => NombreFamilia(m) ?? string.Empty, texto)
                    .ThenBy(m => m.Nombre?.Trim() ?? string.Empty, texto)
                    .ThenBy(m => m.Producto?.Trim(), StringComparer.Ordinal)
                    .Select(m => (m, (HuecoMuestra)null)));
            int numero = 0;
            return ordenadas.Select(x =>
            {
                CodigoBarrasProductoDTO codigo = ServicioEtiquetasProducto.CodigoDeLaUnidad(x.Muestra.Codigos);
                return new EtiquetaMuestraDTO
                {
                    Orden = ++numero,
                    Producto = x.Muestra.Producto?.Trim(),
                    Nombre = x.Muestra.Nombre?.Trim(),
                    Familia = NombreFamilia(x.Muestra),
                    Hueco = x.Hueco == null ? null : CasadorEscaneos.TextoUbicacion(x.Hueco.Pasillo, x.Hueco.Fila, x.Hueco.Columna),
                    Stock = x.Muestra.Stock,
                    CodigoBarras = codigo?.Codigo?.Trim(),
                    CodigoInterno = codigo != null && CodigoBarrasInterno.EsInterno(codigo.Codigo)
                };
            }).ToList();
        }

        /// <summary>El nombre de la familia o, si no lo tiene, su código.</summary>
        public static string NombreFamilia(MuestraAlmacen muestra)
        {
            string nombre = muestra?.NombreFamilia?.Trim();
            if (!string.IsNullOrEmpty(nombre))
            {
                return nombre;
            }
            string codigo = muestra?.Familia?.Trim();
            return string.IsNullOrEmpty(codigo) ? null : codigo;
        }

        private static HashSet<string> Pedidos(IEnumerable<string> productos)
            => new HashSet<string>((productos ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// NestoAPI#884: permisos. Dar código en bloque: Almacén, Compras, Dirección e Informática (una tienda da el de un producto
    /// suelto, como siempre, pero no el de todas). Etiquetas de cajón y hoja de muestras: los mismos que las etiquetas de producto
    /// (<see cref="ServicioCodigosBarras.PuedeEscribir"/>: Compras, Almacén, Tiendas, Dirección e Informática).
    /// </summary>
    public static class PermisosMuestras
    {
        public const string MENSAJE_SIN_PERMISO_BLOQUE =
            "Para dar código a todas las muestras hay que ser de Almacén, Compras, Dirección o Informática.";
        public const string MENSAJE_SIN_PERMISO =
            "Para imprimir las etiquetas o la hoja de muestras hay que ser de Compras, Almacén, Tiendas, Dirección o Informática.";

        private static readonly string[] gruposBloque =
        {
            Constantes.GruposSeguridad.ALMACEN,
            Constantes.GruposSeguridad.COMPRAS,
            Constantes.GruposSeguridad.DIRECCION,
            Constantes.GruposSeguridad.INFORMATICA
        };

        public static bool PuedeDarCodigoEnBloque(IPrincipal usuario)
            => usuario != null && gruposBloque.Any(g => usuario.IsInRoleSinDominio(g));

        public static bool PuedeImprimir(IPrincipal usuario) => ServicioCodigosBarras.PuedeEscribir(usuario);
    }

    public interface IServicioCodigoInternoMuestras
    {
        Task<ResultadoCodigoInternoMuestrasDTO> Dar(string empresa, string usuario, bool ensayo);
    }

    /// <summary>
    /// NestoAPI#884 (a): el código interno del 29 EN BLOQUE a las muestras activas sin ningún código. Cada una por el único camino
    /// que da internos (<see cref="IServicioCodigosBarras.AsignarInterno"/> → <see cref="RepositorioCodigosBarras.GuardarInternoEnExclusiva"/>,
    /// con su bloqueo de aplicación), así que es idempotente: la que ya lo tiene ni entra. Una que falla no para las demás. Rastro:
    /// UNA fila en Modificaciones (Tabla <see cref="TABLA_RASTRO"/>) con lo dado.
    /// </summary>
    public class ServicioCodigoInternoMuestras : IServicioCodigoInternoMuestras
    {
        public const string TABLA_RASTRO = "CodigoInternoMuestras";

        private readonly IRepositorioMuestras repositorio;
        private readonly IServicioCodigosBarras codigos;

        public ServicioCodigoInternoMuestras(IRepositorioMuestras repositorio, IServicioCodigosBarras codigos)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.codigos = codigos ?? throw new ArgumentNullException(nameof(codigos));
        }

        public async Task<ResultadoCodigoInternoMuestrasDTO> Dar(string empresa, string usuario, bool ensayo)
        {
            string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            List<MuestraAlmacen> bloque = ReglasMuestras.ParaElBloque(await repositorio.LeerMuestras(emp, null).ConfigureAwait(false));
            var resultado = new ResultadoCodigoInternoMuestrasDTO
            {
                Ensayo = ensayo,
                Total = bloque.Count,
                Muestras = bloque.Select(m => new MuestraCodigoInternoDTO
                {
                    Producto = m.Producto.Trim(),
                    Nombre = m.Nombre?.Trim(),
                    Familia = ReglasMuestras.NombreFamilia(m),
                    // En el ensayo, el de la referencia (si otro producto lo tuviera, sería del contador: no debería pasar)
                    Codigo = ensayo ? CodigoBarrasInterno.DeReferencia(m.Producto) : null
                }).ToList()
            };
            if (bloque.Count == 0)
            {
                resultado.Mensaje = "Todas las muestras activas tienen ya algún código de barras: no hay que dar ninguno.";
                return resultado;
            }
            if (ensayo)
            {
                resultado.Mensaje = $"Se daría el código interno a {Muestras(bloque.Count)} sin ningún código de barras. No se ha guardado nada (ensayo).";
                return resultado;
            }

            foreach (MuestraCodigoInternoDTO muestra in resultado.Muestras)
            {
                try
                {
                    ResultadoCodigoBarras dado = await codigos.AsignarInterno(emp, muestra.Producto, usuario, false).ConfigureAwait(false);
                    if (dado.Estado == EstadoOperacionCodigoBarras.Creado || dado.Estado == EstadoOperacionCodigoBarras.Ok)
                    {
                        muestra.Codigo = dado.Codigo?.Codigo?.Trim();
                        muestra.Asignado = dado.Estado == EstadoOperacionCodigoBarras.Creado;
                    }
                    else
                    {
                        muestra.Error = dado.Mensaje ?? "No se ha podido dar el código.";
                    }
                }
                catch (Exception ex)
                {
                    muestra.Error = ex.Message;
                    ElmahHelper.Log(new Exception($"[Muestras #884] No se ha podido dar el código interno al {muestra.Producto}: {ex.Message}", ex));
                }
            }
            resultado.Asignados = resultado.Muestras.Count(m => m.Asignado);
            resultado.Errores = resultado.Muestras.Count(m => m.Error != null);
            resultado.Mensaje = $"Código interno dado a {Muestras(resultado.Asignados)}." +
                (resultado.Errores > 0 ? $" {Muestras(resultado.Errores)} no se han podido hacer: vuelva a intentarlo (las hechas no se repiten)." : string.Empty);
            if (resultado.Asignados > 0)
            {
                await Rastro(emp, usuario, resultado).ConfigureAwait(false);
            }
            return resultado;
        }

        /// <summary>Nunca rompe: los códigos ya están dados.</summary>
        private async Task Rastro(string empresa, string usuario, ResultadoCodigoInternoMuestrasDTO resultado)
        {
            try
            {
                string nuevo = JsonConvert.SerializeObject(new
                {
                    Issue = "NestoAPI#884",
                    Empresa = empresa,
                    Usuario = usuario,
                    resultado.Asignados,
                    Codigos = resultado.Muestras.Where(m => m.Asignado).Select(m => new { m.Producto, m.Codigo }),
                    Errores = resultado.Muestras.Where(m => m.Error != null).Select(m => new { m.Producto, m.Error })
                });
                await repositorio.ApuntarRastro(TABLA_RASTRO, null, nuevo, usuario).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Muestras #884] Códigos internos dados pero sin rastro en Modificaciones: {ex.Message}", ex));
            }
        }

        private static string Muestras(int n) => n == 1 ? "1 muestra" : $"{n} muestras";
    }

    public interface IServicioEtiquetasMuestras
    {
        /// <exception cref="ArgumentException">Almacén o impresora pedida que no valen o demasiadas etiquetas: el motivo es para el usuario.</exception>
        /// <exception cref="ImpresionEtiquetasException">Windows no ha dejado imprimir.</exception>
        Task<ResultadoEtiquetasMuestrasDTO> Imprimir(string empresa, PeticionEtiquetasMuestrasDTO peticion, string usuario, bool ensayo);
    }

    /// <summary>
    /// NestoAPI#884 (b): las etiquetas de cajón (REI, ALC) o gaveta (ALG) de las muestras de un almacén, POR LOTES y en el orden en
    /// que se pegan (<see cref="ReglasMuestras.Etiquetas"/>), en UN trabajo a la impresora pedida o, sin ella, a la DEL ALMACÉN (<see cref="ReglaImpresoraEtiquetas"/>, Carlos 25/11/26; la misma que la
    /// de producto). Con ensayo, el listado de lo que saldría sin imprimir ni dar códigos. De verdad, a la muestra que aún no tiene
    /// código se le da el interno (el mismo camino que la etiqueta de producto) ANTES de imprimir y después de mirar la impresora.
    /// </summary>
    public class ServicioEtiquetasMuestras : IServicioEtiquetasMuestras
    {
        private readonly IRepositorioMuestras repositorio;
        private readonly ILectorOrdenRecorrido ordenRecorrido;
        private readonly ILectorParametrosUsuario parametros;
        private readonly IImpresoraEtiquetasAlmacen impresora;
        private readonly IServicioCodigosBarras codigos;

        public ServicioEtiquetasMuestras(IRepositorioMuestras repositorio, ILectorOrdenRecorrido ordenRecorrido, ILectorParametrosUsuario parametros,
            IImpresoraEtiquetasAlmacen impresora, IServicioCodigosBarras codigos)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ordenRecorrido = ordenRecorrido;
            this.parametros = parametros ?? throw new ArgumentNullException(nameof(parametros));
            this.impresora = impresora ?? throw new ArgumentNullException(nameof(impresora));
            this.codigos = codigos ?? throw new ArgumentNullException(nameof(codigos));
        }

        public async Task<ResultadoEtiquetasMuestrasDTO> Imprimir(string empresa, PeticionEtiquetasMuestrasDTO peticion, string usuario, bool ensayo)
        {
            string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string almacen = ReglasMuestras.NormalizarAlmacen(peticion?.Almacen)
                ?? throw new ArgumentException(string.IsNullOrWhiteSpace(peticion?.Almacen) ? "Falta el almacén (ALG, REI o ALC)." : ReglasMuestras.MensajeAlmacen);
            // Carlos 25/11/26: la pedida o, sin ella, la del ALMACÉN de las muestras (no la de quien imprime). Antes de leer nada:
            // una impresora que no vale es un 400 también en el ensayo
            ImpresoraEtiquetasElegida elegida = ReglaImpresoraEtiquetas.Elegir(peticion.Impresora, almacen,
                ReglaImpresoraEtiquetas.LectorDefecto(parametros), () => ServicioEtiquetasHueco.ImpresoraDelUsuario(parametros, emp, usuario));

            List<MuestraAlmacen> todas = await repositorio.LeerMuestras(emp, almacen).ConfigureAwait(false) ?? new List<MuestraAlmacen>();
            List<MuestraAlmacen> filtradas = ReglasMuestras.Filtrar(todas, peticion.Familia, peticion.SoloConStock, peticion.Productos);
            OrdenRecorrido orden = ordenRecorrido == null ? OrdenRecorrido.ComoHoy : await ordenRecorrido.DelAlmacen(emp, almacen).ConfigureAwait(false);
            List<EtiquetaMuestraDTO> etiquetas = ReglasMuestras.Etiquetas(filtradas, orden);
            var resultado = new ResultadoEtiquetasMuestrasDTO
            {
                Almacen = almacen,
                Ensayo = ensayo,
                Impresora = elegida.Impresora,
                OrigenImpresora = elegida.Origen,
                Total = etiquetas.Count,
                Etiquetas = etiquetas,
                NoSonMuestras = ReglasMuestras.NoSonMuestras(todas, peticion.Productos),
                SinCodigo = etiquetas.Where(e => e.CodigoBarras == null).Select(e => e.Producto).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            };
            string noSon = resultado.NoSonMuestras.Count == 0 ? string.Empty
                : $" No son muestras activas (no salen): {string.Join(", ", resultado.NoSonMuestras)}.";
            if (etiquetas.Count == 0)
            {
                resultado.Mensaje = "No hay muestras con esos filtros." + noSon;
                return resultado;
            }
            if (etiquetas.Count > ReglasMuestras.MAXIMO_ETIQUETAS)
            {
                throw new ArgumentException($"Son {etiquetas.Count} etiquetas y como mucho se imprimen {ReglasMuestras.MAXIMO_ETIQUETAS} de una vez: " +
                    "elija una familia o pida solo las que tienen stock.");
            }

            if (ensayo)
            {
                foreach (EtiquetaMuestraDTO etiqueta in etiquetas)
                {
                    etiqueta.Lineas = GeneradorEtiquetasProducto.ComponerContenedor(etiqueta.Producto, etiqueta.Nombre,
                        etiqueta.CodigoBarras ?? CodigoBarrasInterno.DeReferencia(etiqueta.Producto) ?? CodigoBarrasInterno.DeContador(1)).Lineas;
                }
                resultado.Mensaje = $"Saldrían {Etiquetas(etiquetas.Count)} de muestras de {almacen} en {resultado.Impresora}" +
                    (resultado.SinCodigo > 0 ? $"; a {resultado.SinCodigo} sin código se les daría el interno al imprimir" : string.Empty) + "." + noSon;
                return resultado;
            }

            // La impresora ya está elegida (y validada) antes de dar ningún código
            string destino = elegida.Impresora;
            var dados = new Dictionary<string, CodigoBarrasProductoDTO>(StringComparer.OrdinalIgnoreCase);
            foreach (string producto in etiquetas.Where(e => e.CodigoBarras == null).Select(e => e.Producto).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ResultadoCodigoBarras interno = await codigos.AsignarInterno(emp, producto, usuario, false).ConfigureAwait(false);
                if (interno.Codigo == null || (interno.Estado != EstadoOperacionCodigoBarras.Creado && interno.Estado != EstadoOperacionCodigoBarras.Ok))
                {
                    throw new ArgumentException(interno.Mensaje ?? $"No se ha podido dar el código interno al {producto}.");
                }
                dados[producto] = interno.Codigo;
                if (interno.Estado == EstadoOperacionCodigoBarras.Creado)
                {
                    resultado.CodigosAsignados++;
                }
            }
            var trabajo = new List<EtiquetaContenedorMuestra>();
            foreach (EtiquetaMuestraDTO etiqueta in etiquetas)
            {
                if (etiqueta.CodigoBarras == null && dados.TryGetValue(etiqueta.Producto, out CodigoBarrasProductoDTO dado))
                {
                    etiqueta.CodigoBarras = dado.Codigo?.Trim();
                    etiqueta.CodigoInterno = true;
                    etiqueta.CodigoAsignado = true;
                }
                EtiquetaContenedorMuestra compuesta = GeneradorEtiquetasProducto.ComponerContenedor(etiqueta.Producto, etiqueta.Nombre, etiqueta.CodigoBarras);
                etiqueta.Lineas = compuesta.Lineas;
                trabajo.Add(compuesta);
            }
            _ = impresora.Imprimir(destino, GeneradorEtiquetasProducto.TrabajoContenedores(trabajo));
            resultado.Impresas = trabajo.Count;
            resultado.SinCodigo = 0;
            resultado.Mensaje = $"{Etiquetas(trabajo.Count)} de muestras de {almacen} mandada{(trabajo.Count == 1 ? string.Empty : "s")} a {destino}" +
                (resultado.CodigosAsignados > 0 ? $"; se ha dado el código interno a {resultado.CodigosAsignados}" : string.Empty) + "." + noSon;
            return resultado;
        }

        private static string Etiquetas(int n) => n == 1 ? "1 etiqueta" : $"{n} etiquetas";
    }

    /// <summary>NestoAPI#884: las muestras con SQL a mano, sin NOLOCK en nada que se escriba después (solo se lee).</summary>
    public class RepositorioMuestrasSql : IRepositorioMuestras, IDisposable
    {
        internal const string SQL_MUESTRAS = @"
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, RTRIM(p.Familia) AS Familia,
       RTRIM(f.[Descripción]) AS NombreFamilia, RTRIM(p.SubGrupo) AS SubGrupo, p.Estado AS Estado,
       RTRIM(p.CodBarras) AS CodBarras, CAST(ISNULL(s.Stock, 0) AS int) AS Stock
FROM Productos p WITH (NOLOCK)
     LEFT JOIN Familias f WITH (NOLOCK) ON f.Empresa = p.Empresa AND f.[Número] = p.Familia
     OUTER APPLY (SELECT SUM(e.Cantidad) AS Stock FROM ExtractoProducto e WITH (NOLOCK)
                  WHERE e.Empresa IN (@empresa, @espejo) AND e.[Número] = p.[Número] AND e.[Almacén] = @almacen) s
WHERE p.Empresa = @empresa AND p.SubGrupo = @subgrupo AND p.Estado >= 0";

        internal const string SQL_CODIGOS = @"
SELECT RTRIM(c.Producto) AS Producto, RTRIM(c.Codigo) AS Codigo, c.Cantidad AS Cantidad, c.Principal AS Principal, c.Activo AS Activo
FROM ProductosCodigosBarras c WITH (NOLOCK)
     JOIN Productos p WITH (NOLOCK) ON p.Empresa = c.Empresa AND p.[Número] = c.Producto
WHERE c.Empresa = @empresa AND c.Activo = 1 AND p.SubGrupo = @subgrupo AND p.Estado >= 0";

        // Donde está físicamente: libre (0), reservada para un picking (3) o para una reposición (4)
        internal const string SQL_HUECOS = @"
SELECT RTRIM(u.[Número]) AS Producto, RTRIM(u.Pasillo) AS Pasillo, RTRIM(u.Fila) AS Fila, RTRIM(u.Columna) AS Columna,
       CAST(SUM(u.Cantidad) AS int) AS Cantidad
FROM Ubicaciones u WITH (NOLOCK)
     JOIN Productos p WITH (NOLOCK) ON p.Empresa = u.Empresa AND p.[Número] = u.[Número]
WHERE u.Empresa = @empresa AND u.[Almacén] = @almacen AND u.Estado IN (0, 3, 4)
      AND u.Pasillo IS NOT NULL AND RTRIM(u.Pasillo) <> ''
      AND p.SubGrupo = @subgrupo AND p.Estado >= 0
GROUP BY u.[Número], u.Pasillo, u.Fila, u.Columna
HAVING SUM(u.Cantidad) > 0";

        internal const string SQL_RASTRO = @"
INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario, Fecha) VALUES (@tabla, @anterior, @nuevo, @usuario, GETDATE())";

        private class FilaMuestra
        {
            public string Producto { get; set; }
            public string Nombre { get; set; }
            public string Familia { get; set; }
            public string NombreFamilia { get; set; }
            public string SubGrupo { get; set; }
            public short? Estado { get; set; }
            public string CodBarras { get; set; }
            public int Stock { get; set; }
        }

        private class FilaCodigo
        {
            public string Producto { get; set; }
            public string Codigo { get; set; }
            public int Cantidad { get; set; }
            public bool Principal { get; set; }
            public bool Activo { get; set; }
        }

        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioMuestrasSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public static RepositorioMuestrasSql ConContextoPropio() => new RepositorioMuestrasSql(new NVEntities()) { contextoPropio = true };

        public async Task<List<MuestraAlmacen>> LeerMuestras(string empresa, string almacen)
        {
            List<FilaMuestra> filas = await db.Database.SqlQuery<FilaMuestra>(SQL_MUESTRAS,
                    Char("@empresa", empresa, 3), Char("@espejo", Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO, 3),
                    Char("@almacen", almacen, 3), Char("@subgrupo", Constantes.Productos.SUBGRUPO_MUESTRAS, 3))
                .ToListAsync().ConfigureAwait(false);
            List<FilaCodigo> codigos = await db.Database.SqlQuery<FilaCodigo>(SQL_CODIGOS,
                    Char("@empresa", empresa, 3), Char("@subgrupo", Constantes.Productos.SUBGRUPO_MUESTRAS, 3))
                .ToListAsync().ConfigureAwait(false);
            List<HuecoMuestra> huecos = string.IsNullOrWhiteSpace(almacen) ? new List<HuecoMuestra>()
                : await db.Database.SqlQuery<HuecoMuestra>(SQL_HUECOS,
                        Char("@empresa", empresa, 3), Char("@almacen", almacen, 3), Char("@subgrupo", Constantes.Productos.SUBGRUPO_MUESTRAS, 3))
                    .ToListAsync().ConfigureAwait(false);
            return Juntar(filas, codigos, huecos);
        }

        /// <summary>Cada muestra con sus códigos (el de la ficha como principal si no está ya en la tabla) y sus huecos.</summary>
        private static List<MuestraAlmacen> Juntar(List<FilaMuestra> filas, List<FilaCodigo> codigos, List<HuecoMuestra> huecos)
        {
            ILookup<string, FilaCodigo> codigosPorProducto = codigos.Where(c => !string.IsNullOrWhiteSpace(c.Producto))
                .ToLookup(c => c.Producto.Trim(), StringComparer.OrdinalIgnoreCase);
            ILookup<string, HuecoMuestra> huecosPorProducto = huecos.Where(h => !string.IsNullOrWhiteSpace(h.Producto))
                .ToLookup(h => h.Producto.Trim(), StringComparer.OrdinalIgnoreCase);
            return filas.Where(f => !string.IsNullOrWhiteSpace(f.Producto)).Select(f =>
            {
                string producto = f.Producto.Trim();
                List<CodigoBarrasProductoDTO> suyos = codigosPorProducto[producto]
                    .Select(c => new CodigoBarrasProductoDTO { Codigo = c.Codigo?.Trim(), Cantidad = c.Cantidad, Principal = c.Principal, Activo = c.Activo })
                    .ToList();
                string ficha = f.CodBarras?.Trim();
                if (!string.IsNullOrEmpty(ficha) && !suyos.Any(c => string.Equals(c.Codigo, ficha, StringComparison.OrdinalIgnoreCase)))
                {
                    suyos.Insert(0, new CodigoBarrasProductoDTO { Codigo = ficha, Cantidad = 1, Principal = true, Activo = true, Origen = ServicioCodigosBarras.ORIGEN_FICHA });
                }
                return new MuestraAlmacen
                {
                    Producto = producto,
                    Nombre = f.Nombre?.Trim(),
                    Familia = f.Familia?.Trim(),
                    NombreFamilia = f.NombreFamilia?.Trim(),
                    SubGrupo = f.SubGrupo?.Trim(),
                    Estado = f.Estado,
                    Stock = f.Stock,
                    Codigos = suyos,
                    Huecos = huecosPorProducto[producto].ToList()
                };
            }).ToList();
        }

        public Task ApuntarRastro(string tabla, string anterior, string nuevo, string usuario)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_RASTRO,
                new SqlParameter("@tabla", SqlDbType.NVarChar, 50) { Value = tabla },
                new SqlParameter("@anterior", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                new SqlParameter("@nuevo", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) ?? string.Empty });
        }

        private static SqlParameter Char(string nombre, string valor, int tamano)
            => new SqlParameter(nombre, SqlDbType.Char, tamano) { Value = string.IsNullOrWhiteSpace(valor) ? (object)DBNull.Value : valor.Trim() };

        public void Dispose()
        {
            if (contextoPropio)
            {
                db.Dispose();
            }
        }
    }
}
