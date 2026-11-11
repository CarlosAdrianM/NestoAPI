using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    public enum EstadoInventarioUbicaciones
    {
        Ok,
        PeticionIncorrecta,
        /// <summary>El almacén no existe en la empresa (404).</summary>
        NoExisteAlmacen,
        /// <summary>El almacén no se inventaría así (con o sin ubicaciones): 400.</summary>
        AlmacenNoAdmite,
        /// <summary>Ya hay filas pendientes de eso ese día (409 con las Lineas).</summary>
        YaRellenado,
        /// <summary>NestoAPI#784: eso ya se contó y contabilizó ese día y no se ha pedido OtraVez (409 con las contabilizadas en Lineas).</summary>
        YaContabilizado
    }

    public class ResultadoInventarioUbicaciones<T>
    {
        public EstadoInventarioUbicaciones Estado { get; set; }
        public string Mensaje { get; set; }
        public T Respuesta { get; set; }
        /// <summary>Con <see cref="EstadoInventarioUbicaciones.YaRellenado"/>: las filas pendientes que ya hay.</summary>
        public List<LineaInventarioContadaDTO> Lineas { get; set; } = new List<LineaInventarioContadaDTO>();
    }

    /// <summary>NestoAPI#795: qué filas pendientes se buscan. Lo que va a null no filtra.</summary>
    public class FiltroInventarioContado
    {
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        /// <summary>La referencia (ya leída).</summary>
        public string Producto { get; set; }
        public string Familia { get; set; }

        public bool PorUbicacion => Pasillo != null || Fila != null || Columna != null;
        public bool VacioDelTodo => !PorUbicacion && Producto == null && Familia == null;
    }

    /// <summary>NestoAPI#795: una fila de lo que devuelve prdUbicacionesParaInventario (los nombres de sus columnas).</summary>
    public class UbicacionPrioridadInventario
    {
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int? DiasUltInventario { get; set; }
    }

    /// <summary>NestoAPI#795: una ubicación (pasillo, fila, columna) con un número (productos o filas).</summary>
    public class UbicacionConProductos
    {
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int Productos { get; set; }
    }

    public interface IServicioInventarioUbicaciones
    {
        Task<ResultadoInventarioUbicaciones<List<UbicacionParaInventarioDTO>>> Siguientes(string empresa, string almacen, int numero, DateTime hoy);
        Task<ResultadoInventarioUbicaciones<RellenarInventarioFilasRespuestaDTO>> RellenarUbicacion(RellenarUbicacionInventarioDTO peticion, string usuario);
        /// <param name="producto">La referencia ya leída (el controlador la saca de lo escaneado), o null si va por familia.</param>
        Task<ResultadoInventarioUbicaciones<RellenarInventarioFilasRespuestaDTO>> RellenarParcial(RellenarParcialInventarioDTO peticion, string producto, string usuario);
        Task<ResultadoInventarioUbicaciones<InventarioContadoDTO>> Contado(string empresa, string almacen, DateTime fecha, FiltroInventarioContado filtro);
    }

    public interface IRepositorioInventarioUbicaciones
    {
        /// <summary>Null si el almacén no existe en la empresa.</summary>
        Task<AlmacenInventario> LeerAlmacen(string empresa, string almacen);
        /// <summary>EXEC prdUbicacionesParaInventario @Número (ÚNICO call site del SP; va por la empresa 1 fija).</summary>
        Task<List<UbicacionPrioridadInventario>> UbicacionesParaInventario(int numero);
        /// <summary>Productos distintos por ubicación en Ubicaciones (estado ≥ 0) del almacén.</summary>
        Task<List<UbicacionConProductos>> ProductosPorUbicacion(string almacen);
        /// <summary>Las ubicaciones con filas pendientes (Estado 1, sin traspaso) ese día (Productos = filas).</summary>
        Task<List<UbicacionConProductos>> UbicacionesRellenadas(string empresa, string almacen, DateTime fecha);
        /// <summary>Las filas pendientes (Estado 1, sin traspaso) del almacén ese día que cumplen el filtro.</summary>
        Task<List<LineaInventarioContadaDTO>> LeerContado(string empresa, string almacen, DateTime fecha, FiltroInventarioContado filtro);
        /// <summary>Abre la transacción con <paramref name="recurso"/> bloqueado (sp_getapplock), para no rellenar dos veces lo mismo.</summary>
        Task<IUnidadInventarioUbicaciones> Empezar(string recurso);
    }

    /// <summary>Lo que va dentro de la transacción. Sin <see cref="Confirmar"/>, el Dispose lo deshace.</summary>
    public interface IUnidadInventarioUbicaciones : IDisposable
    {
        Task<List<LineaInventarioContadaDTO>> Pendientes(string empresa, string almacen, DateTime fecha, FiltroInventarioContado filtro);
        /// <summary>NestoAPI#784: las filas contabilizadas (Estado 2, con NºTraspaso) del almacén ese día que cumplen el filtro, con su Traspaso.</summary>
        Task<List<LineaInventarioContadaDTO>> Contabilizadas(string empresa, string almacen, DateTime fecha, FiltroInventarioContado filtro);
        /// <summary>El teórico de la ubicación: Ubicaciones (estado ≥ 0) de ese almacén y ubicación, sumado por producto.</summary>
        Task<List<StockProductoInventario>> TeoricoUbicacion(string empresa, string almacen, string pasillo, string fila, string columna);
        /// <summary>El stock en el almacén (ExtractoProducto de las empresas 1 y 3) de los productos de la familia o de la referencia.</summary>
        Task<List<StockProductoInventario>> Stocks(string empresa, string almacen, string familia, string producto);
        Task Insertar(IList<Inventario> filas);
        void Confirmar();
    }

    /// <summary>
    /// NestoAPI#795 (épica #784, corte 5 de Ariadna#79): lo que necesita la pantalla de inventario de Ariadna. En Algete (con control de
    /// ubicaciones), ubicación a ubicación como la Ariadna vieja: qué ubicaciones tocan (prdUbicacionesParaInventario) y rellenar una
    /// con su teórico de Ubicaciones. En las tiendas, el evolutivo: rellenar solo una familia o una referencia (como #789). Y lo contado.
    /// </summary>
    public class ServicioInventarioUbicaciones : IServicioInventarioUbicaciones
    {
        public const int NUMERO_POR_DEFECTO = 15;
        public const int NUMERO_MAXIMO = 200;

        private readonly IRepositorioInventarioUbicaciones repositorio;

        public ServicioInventarioUbicaciones(IRepositorioInventarioUbicaciones repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<ResultadoInventarioUbicaciones<List<UbicacionParaInventarioDTO>>> Siguientes(string empresa, string almacen, int numero, DateTime hoy)
        {
            if (numero < 1 || numero > NUMERO_MAXIMO)
            {
                return Error<List<UbicacionParaInventarioDTO>>(EstadoInventarioUbicaciones.PeticionIncorrecta,
                    $"El número de ubicaciones tiene que estar entre 1 y {NUMERO_MAXIMO}.");
            }
            ResultadoInventarioUbicaciones<List<UbicacionParaInventarioDTO>> comprobacion =
                await ComprobarAlmacen<List<UbicacionParaInventarioDTO>>(empresa, almacen, conUbicaciones: true).ConfigureAwait(false);
            if (comprobacion != null)
            {
                return comprobacion;
            }
            empresa = Empresa(empresa);
            almacen = almacen.Trim().ToUpperInvariant();

            List<UbicacionPrioridadInventario> prioridad = await repositorio.UbicacionesParaInventario(numero).ConfigureAwait(false)
                ?? new List<UbicacionPrioridadInventario>();
            Dictionary<string, int> productos = (await repositorio.ProductosPorUbicacion(almacen).ConfigureAwait(false) ?? new List<UbicacionConProductos>())
                .GroupBy(u => Clave(u.Pasillo, u.Fila, u.Columna))
                .ToDictionary(g => g.Key, g => g.Sum(u => u.Productos));
            // Épica #784: rellenada = con al menos tantas filas pendientes como productos tiene (no basta la fila de un «Dejar a cero»).
            Dictionary<string, int> rellenadas = (await repositorio.UbicacionesRellenadas(empresa, almacen, hoy.Date).ConfigureAwait(false)
                ?? new List<UbicacionConProductos>())
                .GroupBy(u => Clave(u.Pasillo, u.Fila, u.Columna))
                .ToDictionary(g => g.Key, g => g.Sum(u => u.Productos));

            List<UbicacionParaInventarioDTO> siguientes = prioridad
                .Where(u => u != null)
                .Select(u =>
                {
                    string clave = Clave(u.Pasillo, u.Fila, u.Columna);
                    return new UbicacionParaInventarioDTO
                    {
                        Pasillo = u.Pasillo?.Trim(),
                        Fila = u.Fila?.Trim(),
                        Columna = u.Columna?.Trim(),
                        DiasUltInventario = u.DiasUltInventario ?? 0,
                        Productos = productos.TryGetValue(clave, out int n) ? n : 0,
                        YaRellenadaHoy = rellenadas.TryGetValue(clave, out int filasHoy) && filasHoy >= (productos.TryGetValue(clave, out int p) ? p : 0)
                    };
                })
                .ToList();
            return Ok(siguientes);
        }

        public async Task<ResultadoInventarioUbicaciones<RellenarInventarioFilasRespuestaDTO>> RellenarUbicacion(RellenarUbicacionInventarioDTO peticion, string usuario)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Almacen))
            {
                return Error<RellenarInventarioFilasRespuestaDTO>(EstadoInventarioUbicaciones.PeticionIncorrecta, "Falta el almacén.");
            }
            string pasillo = Limpio(peticion.Pasillo);
            string fila = Limpio(peticion.Fila);
            string columna = Limpio(peticion.Columna);
            if (pasillo == null || fila == null || columna == null)
            {
                return Error<RellenarInventarioFilasRespuestaDTO>(EstadoInventarioUbicaciones.PeticionIncorrecta, "Falta el pasillo, la fila o la columna de la ubicación.");
            }
            string empresa = Empresa(peticion.Empresa);
            string almacen = peticion.Almacen.Trim().ToUpperInvariant();
            DateTime fecha = (peticion.Fecha ?? DateTime.Today).Date;
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);

            ResultadoInventarioUbicaciones<RellenarInventarioFilasRespuestaDTO> comprobacion =
                await ComprobarAlmacen<RellenarInventarioFilasRespuestaDTO>(empresa, almacen, conUbicaciones: true).ConfigureAwait(false);
            if (comprobacion != null)
            {
                return comprobacion;
            }

            var filtro = new FiltroInventarioContado { Pasillo = pasillo, Fila = fila, Columna = columna };
            using (IUnidadInventarioUbicaciones unidad = await repositorio.Empezar($"InventarioRellenar:{empresa}:{almacen}:{pasillo}:{fila}:{columna}").ConfigureAwait(false))
            {
                List<LineaInventarioContadaDTO> pendientes = await unidad.Pendientes(empresa, almacen, fecha, filtro).ConfigureAwait(false)
                    ?? new List<LineaInventarioContadaDTO>();

                // Épica #784 (11/11/26): «ya rellenada» es que TODOS los productos de la ubicación tienen ya su fila pendiente de ese día.
                // Antes bastaba una: tras «Dejar a cero» una referencia (que crea solo su fila), los demás productos de la ubicación se
                // quedaban sin la suya. Ahora se crean solo las que faltan, sin duplicar las que ya hay.
                List<StockProductoInventario> teorico = await unidad.TeoricoUbicacion(empresa, almacen, pasillo, fila, columna).ConfigureAwait(false);
                List<Inventario> filas = FilasQueFaltan(FilasDeUbicacion(teorico, empresa, almacen, fecha, usuarioAuditoria, pasillo, fila, columna), pendientes);
                if (pendientes.Count > 0 && filas.Count == 0)
                {
                    return YaRellenado<RellenarInventarioFilasRespuestaDTO>(
                        $"La ubicación {Ubicacion(pasillo, fila, columna)} ya está rellenada el {Dia(fecha)}: tiene {Filas(pendientes.Count)} sin contabilizar.",
                        pendientes);
                }

                var lineas = pendientes;
                if (filas.Count > 0)
                {
                    await unidad.Insertar(filas).ConfigureAwait(false);
                    lineas = await unidad.Pendientes(empresa, almacen, fecha, filtro).ConfigureAwait(false) ?? lineas;
                }
                unidad.Confirmar();

                return Ok(new RellenarInventarioFilasRespuestaDTO
                {
                    Almacen = almacen,
                    Fecha = fecha,
                    Pasillo = pasillo,
                    Fila = fila,
                    Columna = columna,
                    Filas = filas.Count,
                    Unidades = filas.Sum(f => f.StockCalculado ?? 0),
                    Usuario = usuarioAuditoria,
                    Lineas = lineas
                });
            }
        }

        public async Task<ResultadoInventarioUbicaciones<RellenarInventarioFilasRespuestaDTO>> RellenarParcial(RellenarParcialInventarioDTO peticion, string producto, string usuario)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Almacen))
            {
                return Error<RellenarInventarioFilasRespuestaDTO>(EstadoInventarioUbicaciones.PeticionIncorrecta, "Falta el almacén.");
            }
            string familia = string.IsNullOrWhiteSpace(peticion.Familia) ? null : peticion.Familia.Trim();
            producto = Limpio(producto);
            if ((familia == null) == (producto == null))
            {
                return Error<RellenarInventarioFilasRespuestaDTO>(EstadoInventarioUbicaciones.PeticionIncorrecta,
                    "Indique la familia o la referencia del evolutivo (una de las dos, no ambas).");
            }
            string empresa = Empresa(peticion.Empresa);
            string almacen = peticion.Almacen.Trim().ToUpperInvariant();
            DateTime fecha = (peticion.Fecha ?? DateTime.Today).Date;
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);

            ResultadoInventarioUbicaciones<RellenarInventarioFilasRespuestaDTO> comprobacion =
                await ComprobarAlmacen<RellenarInventarioFilasRespuestaDTO>(empresa, almacen, conUbicaciones: false).ConfigureAwait(false);
            if (comprobacion != null)
            {
                return comprobacion;
            }

            var filtro = new FiltroInventarioContado { Familia = familia, Producto = producto };
            string queEs = familia != null ? $"la familia {familia}" : $"el producto {producto}";
            // El mismo recurso que el rellenado entero (#789): un parcial y un total del mismo almacén no van a la vez.
            using (IUnidadInventarioUbicaciones unidad = await repositorio.Empezar($"InventarioRellenar:{empresa}:{almacen}").ConfigureAwait(false))
            {
                List<LineaInventarioContadaDTO> pendientes = await unidad.Pendientes(empresa, almacen, fecha, filtro).ConfigureAwait(false)
                    ?? new List<LineaInventarioContadaDTO>();
                if (pendientes.Count > 0)
                {
                    return YaRellenado<RellenarInventarioFilasRespuestaDTO>(
                        $"El inventario de {queEs} en {almacen} del {Dia(fecha)} ya está rellenado: hay {Filas(pendientes.Count)} sin contabilizar.",
                        pendientes);
                }

                // NestoAPI#784 (09/11/26, el 42341 de Paloma en ALC): lo contado y contabilizado ese día no se vuelve a abrir sin querer,
                // porque la fila nueva lleva como teórico el stock de ahora (lo contado) y, si no se cuenta, contabilizarla lo deja a cero.
                if (!peticion.OtraVez)
                {
                    List<LineaInventarioContadaDTO> contabilizadas = await unidad.Contabilizadas(empresa, almacen, fecha, filtro).ConfigureAwait(false)
                        ?? new List<LineaInventarioContadaDTO>();
                    if (contabilizadas.Count > 0)
                    {
                        return new ResultadoInventarioUbicaciones<RellenarInventarioFilasRespuestaDTO>
                        {
                            Estado = EstadoInventarioUbicaciones.YaContabilizado,
                            Mensaje = MensajeYaContabilizado(familia, producto, almacen, fecha, contabilizadas),
                            Lineas = contabilizadas
                        };
                    }
                }

                List<StockProductoInventario> stocks = await unidad.Stocks(empresa, almacen, familia, producto).ConfigureAwait(false)
                    ?? new List<StockProductoInventario>();
                List<Inventario> filas;
                if (producto != null)
                {
                    StockProductoInventario stock = stocks.FirstOrDefault(s => s != null && string.Equals(s.Numero?.Trim(), producto, StringComparison.OrdinalIgnoreCase));
                    if (stock == null)
                    {
                        return Error<RellenarInventarioFilasRespuestaDTO>(EstadoInventarioUbicaciones.PeticionIncorrecta, $"No existe el producto {producto} en la empresa {empresa}.");
                    }
                    if (stock.Ficticio)
                    {
                        return Error<RellenarInventarioFilasRespuestaDTO>(EstadoInventarioUbicaciones.PeticionIncorrecta, $"El producto {producto} es ficticio: no se inventaría.");
                    }
                    filas = new List<Inventario> { Fila(stock, empresa, almacen, fecha, usuarioAuditoria, stock.Stock) };
                }
                else
                {
                    // La regla del rellenado entero (#789): no ficticios con stock distinto de cero.
                    filas = ServicioRellenarInventario.FilasARellenar(stocks, empresa, almacen, fecha, usuarioAuditoria);
                }

                var lineas = new List<LineaInventarioContadaDTO>();
                if (filas.Count > 0)
                {
                    await unidad.Insertar(filas).ConfigureAwait(false);
                    lineas = await unidad.Pendientes(empresa, almacen, fecha, filtro).ConfigureAwait(false) ?? lineas;
                }
                unidad.Confirmar();

                return Ok(new RellenarInventarioFilasRespuestaDTO
                {
                    Almacen = almacen,
                    Fecha = fecha,
                    Familia = familia,
                    Producto = producto,
                    Filas = filas.Count,
                    Unidades = filas.Sum(f => f.StockCalculado ?? 0),
                    Usuario = usuarioAuditoria,
                    Lineas = lineas
                });
            }
        }

        public async Task<ResultadoInventarioUbicaciones<InventarioContadoDTO>> Contado(string empresa, string almacen, DateTime fecha, FiltroInventarioContado filtro)
        {
            if (string.IsNullOrWhiteSpace(almacen))
            {
                return Error<InventarioContadoDTO>(EstadoInventarioUbicaciones.PeticionIncorrecta, "Falta el almacén.");
            }
            empresa = Empresa(empresa);
            almacen = almacen.Trim().ToUpperInvariant();
            filtro = filtro ?? new FiltroInventarioContado();
            filtro.Pasillo = Limpio(filtro.Pasillo);
            filtro.Fila = Limpio(filtro.Fila);
            filtro.Columna = Limpio(filtro.Columna);
            filtro.Producto = Limpio(filtro.Producto);
            filtro.Familia = string.IsNullOrWhiteSpace(filtro.Familia) ? null : filtro.Familia.Trim();

            List<LineaInventarioContadaDTO> lineas = await repositorio.LeerContado(empresa, almacen, fecha.Date, filtro).ConfigureAwait(false)
                ?? new List<LineaInventarioContadaDTO>();
            var contado = new InventarioContadoDTO { Almacen = almacen, Fecha = fecha.Date };
            if (filtro.VacioDelTodo)
            {
                contado.Ubicaciones = ResumenPorUbicacion(lineas);
            }
            else
            {
                contado.Lineas = lineas
                    .OrderBy(l => l.Pasillo, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(l => l.Fila, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(l => l.Columna, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(l => l.Producto, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            return Ok(contado);
        }

        /// <summary>NestoAPI#784: «El 42341 ya se contó y contabilizó hoy en ALC (contado 2, traspaso 81350). Si de verdad...».</summary>
        internal static string MensajeYaContabilizado(string familia, string producto, string almacen, DateTime fecha, List<LineaInventarioContadaDTO> contabilizadas)
        {
            string cuando = Cuando(fecha);
            if (familia == null)
            {
                LineaInventarioContadaDTO ultima = contabilizadas.OrderBy(l => l.Traspaso ?? 0).ThenBy(l => l.NumeroOrden).Last();
                string veces = contabilizadas.Count == 1 ? string.Empty : $"{contabilizadas.Count} veces; la última, ";
                return $"El {producto} ya se contó y contabilizó {cuando} en {almacen} ({veces}contado {ultima.StockReal}, traspaso {ultima.Traspaso}). " +
                    "Si de verdad hay que volver a contarlo, pulse «Contar otra vez».";
            }
            return $"La familia {familia} ya se contó y contabilizó {cuando} en {almacen} ({Filas(contabilizadas.Count)}, " +
                $"{Traspasos(contabilizadas.Select(l => l.Traspaso))}). Si de verdad hay que volver a contarla, pulse «Contar otra vez».";
        }

        /// <summary>«hoy» o «el 21/11/2026».</summary>
        internal static string Cuando(DateTime fecha) => fecha.Date == DateTime.Today ? "hoy" : $"el {Dia(fecha)}";

        /// <summary>«traspaso 81350», «traspasos 81350 y 81351», «traspasos 1, 2 y 3».</summary>
        internal static string Traspasos(IEnumerable<int?> traspasos)
        {
            List<string> distintos = traspasos.Where(t => t.HasValue).Select(t => t.Value).Distinct().OrderBy(t => t)
                .Select(t => t.ToString(CultureInfo.InvariantCulture)).ToList();
            if (distintos.Count == 0)
            {
                return "sin traspaso";
            }
            if (distintos.Count == 1)
            {
                return "traspaso " + distintos[0];
            }
            return "traspasos " + string.Join(", ", distintos.Take(distintos.Count - 1)) + " y " + distintos.Last();
        }

        internal static List<ResumenUbicacionContadaDTO> ResumenPorUbicacion(IEnumerable<LineaInventarioContadaDTO> lineas)
        {
            return lineas
                .GroupBy(l => Clave(l.Pasillo, l.Fila, l.Columna))
                .Select(g => new ResumenUbicacionContadaDTO
                {
                    Pasillo = g.First().Pasillo?.Trim(),
                    Fila = g.First().Fila?.Trim(),
                    Columna = g.First().Columna?.Trim(),
                    Filas = g.Count(),
                    Unidades = g.Sum(l => l.StockReal),
                    UnidadesCalculadas = g.Sum(l => l.StockCalculado),
                    Usuarios = g.Select(l => l.Usuario?.Trim()).Where(u => !string.IsNullOrEmpty(u))
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList(),
                    UltimaModificacion = g.Max(l => l.FechaModificacion)
                })
                .OrderBy(r => r.Pasillo, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Fila, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Columna, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Como la Ariadna vieja: una fila por producto de la ubicación, StockCalculado su cantidad (aunque sea 0), StockReal 0.</summary>
        internal static List<Inventario> FilasDeUbicacion(IEnumerable<StockProductoInventario> teorico, string empresa, string almacen, DateTime fecha,
            string usuario, string pasillo, string fila, string columna)
        {
            return (teorico ?? Enumerable.Empty<StockProductoInventario>())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Numero))
                .GroupBy(s => s.Numero.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    Inventario linea = Fila(g.First(), empresa, almacen, fecha, usuario, g.Sum(s => s.Stock));
                    linea.Pasillo = pasillo;
                    linea.Fila = fila;
                    linea.Columna = columna;
                    return linea;
                })
                .ToList();
        }

        /// <summary>Épica #784: de las filas del teórico, solo las de productos que aún no tienen fila pendiente en la ubicación ese día.</summary>
        internal static List<Inventario> FilasQueFaltan(IEnumerable<Inventario> filas, IEnumerable<LineaInventarioContadaDTO> pendientes)
        {
            var yaTienen = new HashSet<string>((pendientes ?? Enumerable.Empty<LineaInventarioContadaDTO>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Producto))
                .Select(p => p.Producto.Trim()), StringComparer.OrdinalIgnoreCase);
            return (filas ?? Enumerable.Empty<Inventario>()).Where(f => !yaTienen.Contains(f.Número?.Trim() ?? string.Empty)).ToList();
        }

        private static Inventario Fila(StockProductoInventario producto, string empresa, string almacen, DateTime fecha, string usuario, int stock)
        {
            return new Inventario
            {
                Empresa = empresa,
                Almacén = almacen,
                Fecha = fecha.Date,
                Número = producto.Numero.Trim(),
                Descripción = producto.Descripcion?.Trim(),
                Grupo = producto.Grupo?.Trim(),
                Subgrupo = producto.SubGrupo?.Trim(),
                Familia = producto.Familia?.Trim(),
                StockCalculado = stock,
                StockReal = 0,
                Estado = ServicioRellenarInventario.ESTADO_SIN_CONTABILIZAR,
                Aplicacion = ServicioRellenarInventario.APLICACION,
                Usuario = usuario,
                PorCodigoBarras = null
            };
        }

        private async Task<ResultadoInventarioUbicaciones<T>> ComprobarAlmacen<T>(string empresa, string almacen, bool conUbicaciones)
        {
            if (string.IsNullOrWhiteSpace(almacen))
            {
                return Error<T>(EstadoInventarioUbicaciones.PeticionIncorrecta, "Falta el almacén.");
            }
            string codigo = almacen.Trim().ToUpperInvariant();
            AlmacenInventario datos = await repositorio.LeerAlmacen(Empresa(empresa), codigo).ConfigureAwait(false);
            if (datos == null)
            {
                return Error<T>(EstadoInventarioUbicaciones.NoExisteAlmacen, $"El almacén {codigo} no existe en la empresa {Empresa(empresa)}.");
            }
            string nombre = string.IsNullOrWhiteSpace(datos.Nombre) ? codigo : datos.Nombre.Trim();
            if (conUbicaciones && !datos.ControlUbicaciones)
            {
                return Error<T>(EstadoInventarioUbicaciones.AlmacenNoAdmite, $"{nombre} no tiene control de ubicaciones: se inventaría por producto, no por ubicación.");
            }
            if (!conUbicaciones && datos.ControlUbicaciones)
            {
                return Error<T>(EstadoInventarioUbicaciones.AlmacenNoAdmite, $"{nombre} se inventaría por ubicaciones: rellene la ubicación, no la familia ni la referencia.");
            }
            return null;
        }

        internal static string Clave(string pasillo, string fila, string columna) =>
            $"{pasillo?.Trim().ToUpperInvariant()}|{fila?.Trim().ToUpperInvariant()}|{columna?.Trim().ToUpperInvariant()}";

        private static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim().ToUpperInvariant();

        private static string Empresa(string empresa) =>
            string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        private static string Ubicacion(string pasillo, string fila, string columna) => $"{pasillo}-{fila}-{columna}";

        private static string Dia(DateTime fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        private static string Filas(int n) => n == 1 ? "1 fila" : $"{n} filas";

        private static ResultadoInventarioUbicaciones<T> Ok<T>(T respuesta) =>
            new ResultadoInventarioUbicaciones<T> { Estado = EstadoInventarioUbicaciones.Ok, Respuesta = respuesta };

        private static ResultadoInventarioUbicaciones<T> Error<T>(EstadoInventarioUbicaciones estado, string mensaje) =>
            new ResultadoInventarioUbicaciones<T> { Estado = estado, Mensaje = mensaje };

        private static ResultadoInventarioUbicaciones<T> YaRellenado<T>(string mensaje, List<LineaInventarioContadaDTO> lineas) =>
            new ResultadoInventarioUbicaciones<T> { Estado = EstadoInventarioUbicaciones.YaRellenado, Mensaje = mensaje, Lineas = lineas };
    }
}
