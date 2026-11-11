using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>Sugerencia 1185: cuerpo de POST api/Inventarios/DejarACero.</summary>
    public class DejarACeroInventarioDTO
    {
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        /// <summary>La referencia o un código de barras (se lee como en el resto del inventario).</summary>
        public string Producto { get; set; }
        /// <summary>«Contar otra vez»: sin él, si el producto ya se contó y contabilizó hoy en el almacén, 409 YaContabilizado.</summary>
        public bool OtraVez { get; set; }
        /// <summary>
        /// En un almacén con ubicaciones, las filas pendientes de hoy del producto SIN ubicación (no valen: «Productos que se ubican
        /// contados sin ubicación» no deja contabilizar) se borran. Sin él, 409 FilasSinUbicacion con esas filas.
        /// </summary>
        public bool QuitarFilasSinUbicacion { get; set; }
        /// <summary>Solo dice lo que haría (para la confirmación), sin tocar nada. Mismas reglas y mismos 409.</summary>
        public bool Simular { get; set; }
    }

    /// <summary>Sugerencia 1185: lo que se hace (o se haría, al simular) con una ubicación (o con la fila sin ubicación de una tienda).</summary>
    public class LineaDejarACeroDTO
    {
        public const string CREAR = "Crear";
        public const string PONER_A_CERO = "PonerACero";
        public const string YA_ESTABA_A_CERO = "YaEstabaACero";

        /// <summary>Null en las tiendas (y en los productos que no se ubican).</summary>
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        /// <summary>El teórico: lo ubicado (o el stock del almacén, sin ubicaciones) o el de la fila que ya había.</summary>
        public int StockCalculado { get; set; }
        /// <summary>Lo contado que tenía la fila que ya había (0 si se crea).</summary>
        public int ContadoAntes { get; set; }
        /// <summary>La fila que ya había (null si se crea).</summary>
        public int? NumeroOrden { get; set; }
        /// <summary><see cref="CREAR"/>, <see cref="PONER_A_CERO"/> o <see cref="YA_ESTABA_A_CERO"/>.</summary>
        public string Accion { get; set; }
    }

    /// <summary>Sugerencia 1185: respuesta de POST api/Inventarios/DejarACero.</summary>
    public class DejarACeroInventarioRespuestaDTO
    {
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        /// <summary>True: una fila por ubicación; false: una sola fila sin ubicación (tiendas o producto que no se ubica).</summary>
        public bool PorUbicaciones { get; set; }
        public bool Simulado { get; set; }
        /// <summary>El stock del producto en el almacén (ExtractoProducto de las empresas 1 y 3).</summary>
        public int StockAlmacen { get; set; }
        /// <summary>Lo que está en Ubicaciones sin pasillo (pendiente de ubicar): esto no lo toca.</summary>
        public int UnidadesSinUbicar { get; set; }
        public List<LineaDejarACeroDTO> Lineas { get; set; } = new List<LineaDejarACeroDTO>();
        /// <summary>Las filas sin ubicación que se borran (con QuitarFilasSinUbicacion).</summary>
        public List<LineaInventarioContadaDTO> FilasQuitadas { get; set; } = new List<LineaInventarioContadaDTO>();
        public int FilasCreadas { get; set; }
        public int FilasPuestasACero { get; set; }
        /// <summary>Suma del teórico de las líneas: lo que se quitará del stock al contabilizar.</summary>
        public int Unidades { get; set; }
        public string Usuario { get; set; }
        /// <summary>Lo que hay que decirle al usuario además (contabilizar después, lo que queda sin ubicar…). Null si nada.</summary>
        public string Aviso { get; set; }
    }

    /// <summary>Sugerencia 1185: una ubicación del producto en Ubicaciones (estado ≥ 0) con su cantidad.</summary>
    public class UbicacionProductoInventario
    {
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>Sugerencia 1185: el producto para el «a cero» (ficha de la empresa y stock en el almacén).</summary>
    public class ProductoInventarioACero : StockProductoInventario
    {
        public bool Ubicar { get; set; }
    }

    public interface IRepositorioInventarioACero
    {
        /// <summary>Null si el almacén no existe en la empresa.</summary>
        Task<AlmacenInventario> LeerAlmacen(string empresa, string almacen);
        /// <summary>Abre la transacción con <paramref name="recurso"/> bloqueado (sp_getapplock).</summary>
        Task<IUnidadInventarioACero> Empezar(string recurso);
    }

    /// <summary>Lo que va dentro de la transacción. Sin <see cref="Confirmar"/>, el Dispose lo deshace.</summary>
    public interface IUnidadInventarioACero : IDisposable
    {
        /// <summary>Otro sp_getapplock más en la misma transacción (cada ubicación que se toca, como RellenarUbicacion).</summary>
        Task Bloquear(string recurso);
        /// <summary>Null si el producto no existe en la empresa.</summary>
        Task<ProductoInventarioACero> LeerProducto(string empresa, string almacen, string producto);
        /// <summary>Las filas pendientes (Estado 1, sin traspaso) del producto en el almacén ese día.</summary>
        Task<List<LineaInventarioContadaDTO>> Pendientes(string empresa, string almacen, DateTime fecha, string producto);
        /// <summary>Las filas contabilizadas (Estado 2, con traspaso) del producto en el almacén ese día.</summary>
        Task<List<LineaInventarioContadaDTO>> Contabilizadas(string empresa, string almacen, DateTime fecha, string producto);
        /// <summary>Ubicaciones (estado ≥ 0, con pasillo) del producto en el almacén, sumadas por ubicación (sin filtrar empresa, como el SP).</summary>
        Task<List<UbicacionProductoInventario>> Ubicaciones(string almacen, string producto);
        /// <summary>Lo que hay en Ubicaciones (estado ≥ 0) sin pasillo.</summary>
        Task<int> UnidadesSinUbicar(string almacen, string producto);
        Task Insertar(IList<Inventario> filas);
        /// <summary>StockReal = 0 de esas filas, solo si siguen pendientes. Devuelve cuántas.</summary>
        Task<int> PonerACero(IList<int> numerosOrden);
        /// <summary>Borra esas filas, solo si siguen pendientes y sin ubicación. Devuelve cuántas.</summary>
        Task<int> BorrarSinUbicacion(IList<int> numerosOrden);
        void Confirmar();
    }

    public enum EstadoInventarioACero
    {
        Ok,
        PeticionIncorrecta,
        NoExisteAlmacen,
        NoExisteProducto,
        /// <summary>409 YaContabilizado (se repite con OtraVez).</summary>
        YaContabilizado,
        /// <summary>409 FilasDuplicadas: dos filas pendientes del producto en la misma ubicación (o las dos sin ubicación).</summary>
        FilasDuplicadas,
        /// <summary>409 FilasSinUbicacion: en un almacén con ubicaciones, filas del producto sin ubicación (se quitan con QuitarFilasSinUbicacion).</summary>
        FilasSinUbicacion
    }

    public class ResultadoInventarioACero
    {
        public EstadoInventarioACero Estado { get; set; }
        public string Mensaje { get; set; }
        public DejarACeroInventarioRespuestaDTO Respuesta { get; set; }
        public List<LineaInventarioContadaDTO> Lineas { get; set; } = new List<LineaInventarioContadaDTO>();
    }

    /// <summary>Lo que se va a hacer, calculado sin tocar nada (<see cref="ServicioInventarioACero.Planificar"/>).</summary>
    public class PlanInventarioACero
    {
        public ResultadoInventarioACero Conflicto { get; set; }
        public List<LineaDejarACeroDTO> Lineas { get; set; } = new List<LineaDejarACeroDTO>();
        public List<LineaInventarioContadaDTO> Quitar { get; set; } = new List<LineaInventarioContadaDTO>();
        public bool PorUbicaciones { get; set; }
    }

    /// <summary>
    /// Sugerencia 1185 (Alfredo, Carlos 11/11/26): dejar una referencia a cero en TODAS sus ubicaciones de una vez, como el botón de la
    /// ficha del Nesto viejo, por el inventario de siempre: crea (o pone a 0) las filas pendientes de HOY del producto, una por
    /// ubicación con stock (teórico = lo ubicado, contado 0), como si se hubieran rellenado y contado vacías. No contabiliza: eso
    /// sigue siendo «Contabilizar inventario» con sus comprobaciones (prdContabilizarInventario, que es el que toca ExtractoProducto y
    /// Ubicaciones). Contabilizar es del almacén entero (todas sus filas pendientes), así que no hay un «contabilizar solo este
    /// producto» seguro que ofrecer.
    /// <para>Reglas: nunca duplica (la fila de hoy de una ubicación se pone a 0); nunca crea fila sin ubicación en un almacén con
    /// ubicaciones (salvo productos que no se ubican, que van como en las tiendas: una fila con el stock del almacén); las filas sin
    /// ubicación que ya hubiera (el 44901 de la incidencia 1186) dan 409 y se borran solo si se pide; lo ya contabilizado hoy da
    /// 409 YaContabilizado salvo con OtraVez; dos filas en la misma ubicación, 409 FilasDuplicadas.</para>
    /// </summary>
    public class ServicioInventarioACero
    {
        private readonly IRepositorioInventarioACero repositorio;

        public ServicioInventarioACero(IRepositorioInventarioACero repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        /// <param name="producto">La referencia ya leída (el controlador la saca de lo escaneado).</param>
        public async Task<ResultadoInventarioACero> DejarACero(DejarACeroInventarioDTO peticion, string producto, string usuario, DateTime hoy)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Almacen))
            {
                return Error(EstadoInventarioACero.PeticionIncorrecta, "Falta el almacén.");
            }
            producto = string.IsNullOrWhiteSpace(producto) ? null : producto.Trim();
            if (producto == null)
            {
                return Error(EstadoInventarioACero.PeticionIncorrecta, "Falta el producto.");
            }
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            string almacen = peticion.Almacen.Trim().ToUpperInvariant();
            DateTime fecha = hoy.Date;
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);

            AlmacenInventario datosAlmacen = await repositorio.LeerAlmacen(empresa, almacen).ConfigureAwait(false);
            if (datosAlmacen == null)
            {
                return Error(EstadoInventarioACero.NoExisteAlmacen, $"El almacén {almacen} no existe en la empresa {empresa}.");
            }

            // El mismo recurso que el rellenado entero y el parcial: no van a la vez en el mismo almacén.
            using (IUnidadInventarioACero unidad = await repositorio.Empezar($"InventarioRellenar:{empresa}:{almacen}").ConfigureAwait(false))
            {
                ProductoInventarioACero ficha = await unidad.LeerProducto(empresa, almacen, producto).ConfigureAwait(false);
                if (ficha == null)
                {
                    return Error(EstadoInventarioACero.NoExisteProducto, $"No existe el producto {producto} en la empresa {empresa}.");
                }
                if (ficha.Ficticio)
                {
                    return Error(EstadoInventarioACero.PeticionIncorrecta, $"El producto {producto} es ficticio: no se inventaría.");
                }

                if (!peticion.OtraVez)
                {
                    List<LineaInventarioContadaDTO> contabilizadas = await unidad.Contabilizadas(empresa, almacen, fecha, producto).ConfigureAwait(false)
                        ?? new List<LineaInventarioContadaDTO>();
                    if (contabilizadas.Count > 0)
                    {
                        return new ResultadoInventarioACero
                        {
                            Estado = EstadoInventarioACero.YaContabilizado,
                            Mensaje = ServicioInventarioUbicaciones.MensajeYaContabilizado(null, producto, almacen, fecha, contabilizadas),
                            Lineas = contabilizadas
                        };
                    }
                }

                bool porUbicaciones = datosAlmacen.ControlUbicaciones && ficha.Ubicar;
                List<UbicacionProductoInventario> ubicaciones = porUbicaciones
                    ? await unidad.Ubicaciones(almacen, producto).ConfigureAwait(false) ?? new List<UbicacionProductoInventario>()
                    : new List<UbicacionProductoInventario>();
                // Como RellenarUbicacion de Ariadna (que bloquea solo su ubicación): se bloquea cada ubicación ANTES de mirar lo
                // pendiente, para que nadie la rellene entre medias (saldría una fila duplicada). Siempre en el mismo orden.
                foreach (string clave in ubicaciones
                    .Where(u => u != null && !string.IsNullOrWhiteSpace(u.Pasillo))
                    .Select(u => $"{u.Pasillo.Trim().ToUpperInvariant()}:{u.Fila?.Trim().ToUpperInvariant()}:{u.Columna?.Trim().ToUpperInvariant()}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
                {
                    await unidad.Bloquear($"InventarioRellenar:{empresa}:{almacen}:{clave}").ConfigureAwait(false);
                }
                List<LineaInventarioContadaDTO> pendientes = await unidad.Pendientes(empresa, almacen, fecha, producto).ConfigureAwait(false)
                    ?? new List<LineaInventarioContadaDTO>();
                int sinUbicar = datosAlmacen.ControlUbicaciones ? await unidad.UnidadesSinUbicar(almacen, producto).ConfigureAwait(false) : 0;

                PlanInventarioACero plan = Planificar(porUbicaciones, datosAlmacen.ControlUbicaciones, producto, almacen, fecha, ficha.Stock,
                    pendientes, ubicaciones, peticion.QuitarFilasSinUbicacion);
                if (plan.Conflicto != null)
                {
                    return plan.Conflicto;
                }

                var respuesta = new DejarACeroInventarioRespuestaDTO
                {
                    Almacen = almacen,
                    Fecha = fecha,
                    Producto = ficha.Numero?.Trim() ?? producto,
                    Descripcion = ficha.Descripcion?.Trim(),
                    PorUbicaciones = plan.PorUbicaciones,
                    Simulado = peticion.Simular,
                    StockAlmacen = ficha.Stock,
                    UnidadesSinUbicar = sinUbicar,
                    Lineas = plan.Lineas,
                    FilasQuitadas = plan.Quitar,
                    FilasCreadas = plan.Lineas.Count(l => l.Accion == LineaDejarACeroDTO.CREAR),
                    FilasPuestasACero = plan.Lineas.Count(l => l.Accion == LineaDejarACeroDTO.PONER_A_CERO),
                    Unidades = plan.Lineas.Sum(l => l.StockCalculado),
                    Usuario = usuarioAuditoria
                };
                respuesta.Aviso = Aviso(respuesta);

                if (peticion.Simular)
                {
                    return Ok(respuesta); // sin Confirmar: el Dispose deshace (y no se ha escrito nada)
                }

                if (plan.Quitar.Count > 0)
                {
                    int borradas = await unidad.BorrarSinUbicacion(plan.Quitar.Select(l => l.NumeroOrden).ToList()).ConfigureAwait(false);
                    if (borradas != plan.Quitar.Count)
                    {
                        throw new InvalidOperationException("Las filas sin ubicación han cambiado mientras tanto: no se ha tocado nada. Inténtelo de nuevo.");
                    }
                }
                List<int> aCero = plan.Lineas.Where(l => l.Accion == LineaDejarACeroDTO.PONER_A_CERO && l.NumeroOrden.HasValue)
                    .Select(l => l.NumeroOrden.Value).ToList();
                if (aCero.Count > 0)
                {
                    int puestas = await unidad.PonerACero(aCero).ConfigureAwait(false);
                    if (puestas != aCero.Count)
                    {
                        throw new InvalidOperationException("Las filas del inventario han cambiado mientras tanto (alguna se ha contabilizado): no se ha tocado nada. Inténtelo de nuevo.");
                    }
                }
                List<Inventario> nuevas = plan.Lineas.Where(l => l.Accion == LineaDejarACeroDTO.CREAR)
                    .Select(l => FilaNueva(ficha, empresa, almacen, fecha, usuarioAuditoria, l))
                    .ToList();
                if (nuevas.Count > 0)
                {
                    await unidad.Insertar(nuevas).ConfigureAwait(false);
                }
                unidad.Confirmar();
                return Ok(respuesta);
            }
        }

        /// <summary>
        /// La regla, sin tocar nada. <paramref name="porUbicaciones"/>: almacén con ubicaciones y producto que se ubica (una fila por
        /// ubicación); si no, una sola fila sin ubicación con el stock del almacén.
        /// </summary>
        internal static PlanInventarioACero Planificar(bool porUbicaciones, bool almacenConUbicaciones, string producto, string almacen, DateTime fecha,
            int stockAlmacen, IEnumerable<LineaInventarioContadaDTO> pendientes, IEnumerable<UbicacionProductoInventario> ubicaciones,
            bool quitarSinUbicacion)
        {
            List<LineaInventarioContadaDTO> filas = (pendientes ?? Enumerable.Empty<LineaInventarioContadaDTO>()).Where(f => f != null).ToList();
            var plan = new PlanInventarioACero { PorUbicaciones = porUbicaciones };

            // Dos filas en la misma ubicación (o las dos sin ubicación): el SP apuntaría la diferencia dos veces. Que se arregle antes.
            List<LineaInventarioContadaDTO> duplicadas = filas
                .GroupBy(f => ServicioInventarioUbicaciones.Clave(f.Pasillo, f.Fila, f.Columna))
                .Where(g => g.Count() > 1)
                .SelectMany(g => g)
                .OrderBy(f => f.NumeroOrden)
                .ToList();
            if (duplicadas.Count > 0)
            {
                plan.Conflicto = new ResultadoInventarioACero
                {
                    Estado = EstadoInventarioACero.FilasDuplicadas,
                    Mensaje = $"El {producto} tiene más de una fila sin contabilizar {ServicioInventarioUbicaciones.Cuando(fecha)} en {almacen} en la misma " +
                        $"ubicación ({Detalle(duplicadas)}). Hay que dejar solo una antes de seguir: avise a Informática.",
                    Lineas = duplicadas
                };
                return plan;
            }

            if (porUbicaciones)
            {
                List<LineaInventarioContadaDTO> sinUbicacion = filas.Where(f => string.IsNullOrWhiteSpace(f.Pasillo)).ToList();
                if (sinUbicacion.Count > 0 && !quitarSinUbicacion)
                {
                    plan.Conflicto = new ResultadoInventarioACero
                    {
                        Estado = EstadoInventarioACero.FilasSinUbicacion,
                        Mensaje = $"El {producto} tiene {Filas(sinUbicacion.Count)} sin ubicación {ServicioInventarioUbicaciones.Cuando(fecha)} en {almacen} " +
                            $"({Detalle(sinUbicacion)}). En {almacen} se cuenta por ubicaciones y esa fila no deja contabilizar: si lo deja a cero en todas " +
                            "sus ubicaciones, se borrará.",
                        Lineas = sinUbicacion
                    };
                    return plan;
                }
                plan.Quitar = sinUbicacion;

                Dictionary<string, LineaInventarioContadaDTO> porClave = filas.Where(f => !string.IsNullOrWhiteSpace(f.Pasillo))
                    .ToDictionary(f => ServicioInventarioUbicaciones.Clave(f.Pasillo, f.Fila, f.Columna));
                var lineas = new List<LineaDejarACeroDTO>();
                foreach (IGrouping<string, UbicacionProductoInventario> ubicacion in (ubicaciones ?? Enumerable.Empty<UbicacionProductoInventario>())
                    .Where(u => u != null && !string.IsNullOrWhiteSpace(u.Pasillo))
                    .GroupBy(u => ServicioInventarioUbicaciones.Clave(u.Pasillo, u.Fila, u.Columna)))
                {
                    int cantidad = ubicacion.Sum(u => u.Cantidad);
                    if (porClave.ContainsKey(ubicacion.Key) || cantidad == 0)
                    {
                        continue; // la fila que ya hay manda (abajo); sin stock no hace falta fila
                    }
                    UbicacionProductoInventario primera = ubicacion.First();
                    lineas.Add(new LineaDejarACeroDTO
                    {
                        Pasillo = primera.Pasillo.Trim().ToUpperInvariant(),
                        Fila = primera.Fila?.Trim().ToUpperInvariant(),
                        Columna = primera.Columna?.Trim().ToUpperInvariant(),
                        StockCalculado = cantidad,
                        Accion = LineaDejarACeroDTO.CREAR
                    });
                }
                lineas.AddRange(porClave.Values.Select(DeFilaExistente));
                plan.Lineas = lineas
                    .OrderBy(l => l.Pasillo, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(l => l.Fila, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(l => l.Columna, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return plan;
            }

            // Sin ubicaciones (tiendas, o producto que no se ubica): una sola fila sin ubicación.
            LineaInventarioContadaDTO existente = filas.FirstOrDefault(f => string.IsNullOrWhiteSpace(f.Pasillo));
            List<LineaInventarioContadaDTO> conUbicacion = filas.Where(f => !string.IsNullOrWhiteSpace(f.Pasillo)).ToList();
            if (conUbicacion.Count > 0)
            {
                // Un producto que no se ubica con filas por ubicación: no se adivina cuál vale.
                plan.Conflicto = new ResultadoInventarioACero
                {
                    Estado = EstadoInventarioACero.FilasDuplicadas,
                    Mensaje = $"El {producto} no se ubica, pero tiene {Filas(conUbicacion.Count)} con ubicación {ServicioInventarioUbicaciones.Cuando(fecha)} en {almacen} " +
                        $"({Detalle(conUbicacion)}). Hay que dejar solo una fila sin ubicación antes de seguir: avise a Informática.",
                    Lineas = conUbicacion
                };
                return plan;
            }
            if (existente != null)
            {
                plan.Lineas.Add(DeFilaExistente(existente));
            }
            else if (stockAlmacen != 0)
            {
                plan.Lineas.Add(new LineaDejarACeroDTO { StockCalculado = stockAlmacen, Accion = LineaDejarACeroDTO.CREAR });
            }
            return plan;
        }

        internal static string Aviso(DejarACeroInventarioRespuestaDTO respuesta)
        {
            var partes = new List<string>();
            if (respuesta.Lineas.Count == 0 && respuesta.FilasQuitadas.Count == 0)
            {
                partes.Add($"El {respuesta.Producto} no tiene stock en {respuesta.Almacen}{(respuesta.PorUbicaciones ? " en ninguna ubicación" : string.Empty)} " +
                    "ni filas de inventario de hoy: no hay nada que dejar a cero.");
            }
            else
            {
                partes.Add($"Para que el stock se quede a cero hay que contabilizar el inventario de {respuesta.Almacen} de hoy («Contabilizar inventario»).");
                if (respuesta.PorUbicaciones && respuesta.FilasCreadas > 0)
                {
                    partes.Add("Hasta entonces, Ariadna verá esas ubicaciones como ya rellenadas hoy.");
                }
            }
            if (respuesta.UnidadesSinUbicar != 0)
            {
                partes.Add($"Hay {respuesta.UnidadesSinUbicar} {(Math.Abs(respuesta.UnidadesSinUbicar) == 1 ? "unidad" : "unidades")} del {respuesta.Producto} pendientes de ubicar en {respuesta.Almacen}: esto no las toca.");
            }
            return string.Join(" ", partes);
        }

        private static LineaDejarACeroDTO DeFilaExistente(LineaInventarioContadaDTO fila) => new LineaDejarACeroDTO
        {
            Pasillo = string.IsNullOrWhiteSpace(fila.Pasillo) ? null : fila.Pasillo.Trim(),
            Fila = string.IsNullOrWhiteSpace(fila.Fila) ? null : fila.Fila.Trim(),
            Columna = string.IsNullOrWhiteSpace(fila.Columna) ? null : fila.Columna.Trim(),
            StockCalculado = fila.StockCalculado,
            ContadoAntes = fila.StockReal,
            NumeroOrden = fila.NumeroOrden,
            Accion = fila.StockReal == 0 ? LineaDejarACeroDTO.YA_ESTABA_A_CERO : LineaDejarACeroDTO.PONER_A_CERO
        };

        private static Inventario FilaNueva(ProductoInventarioACero producto, string empresa, string almacen, DateTime fecha, string usuario, LineaDejarACeroDTO linea)
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
                StockCalculado = linea.StockCalculado,
                StockReal = 0,
                Estado = ServicioRellenarInventario.ESTADO_SIN_CONTABILIZAR,
                Aplicacion = ServicioRellenarInventario.APLICACION,
                Usuario = usuario,
                Pasillo = linea.Pasillo,
                Fila = linea.Fila,
                Columna = linea.Columna,
                PorCodigoBarras = null
            };
        }

        private static string Filas(int n) => n == 1 ? "1 fila" : $"{n} filas";

        /// <summary>«nº 948787: contado 1, Alfredo; nº 948796 (001-002-005): contado 0, Alfredo».</summary>
        private static string Detalle(IEnumerable<LineaInventarioContadaDTO> filas) => string.Join("; ", filas.Select(f =>
            $"nº {f.NumeroOrden.ToString(CultureInfo.InvariantCulture)}{(string.IsNullOrWhiteSpace(f.Pasillo) ? string.Empty : $" ({f.Pasillo?.Trim()}-{f.Fila?.Trim()}-{f.Columna?.Trim()})")}: " +
            $"contado {f.StockReal.ToString(CultureInfo.InvariantCulture)}, {TextosSinDominio(f.Usuario)}"));

        private static string TextosSinDominio(string usuario)
        {
            string limpio = usuario?.Trim() ?? string.Empty;
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1) : limpio;
        }

        private static ResultadoInventarioACero Ok(DejarACeroInventarioRespuestaDTO respuesta) =>
            new ResultadoInventarioACero { Estado = EstadoInventarioACero.Ok, Respuesta = respuesta };

        private static ResultadoInventarioACero Error(EstadoInventarioACero estado, string mensaje) =>
            new ResultadoInventarioACero { Estado = estado, Mensaje = mensaje };
    }
}
