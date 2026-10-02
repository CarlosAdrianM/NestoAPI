using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Lo que hay que sacar de un documento de salida (picking o traspaso) y lo que ya se ha leído de él.</summary>
    public class RecorridoSalida
    {
        public string Destino { get; set; }
        public List<LineaPickingAlmacenDTO> Lineas { get; set; } = new List<LineaPickingAlmacenDTO>();
        public List<LecturaPickingAlmacen> Lecturas { get; set; } = new List<LecturaPickingAlmacen>();
    }

    /// <summary>Una reposición (traspaso entre almacenes) con la salida del almacén de origen aún sin contabilizar.</summary>
    public class ReposicionPorSalir
    {
        public int Traspaso { get; set; }
        public string Destino { get; set; }
        public int Lineas { get; set; }
        public int Unidades { get; set; }
    }

    /// <summary>Lo que sale del almacén de origen en un traspaso de reposición, con sus huecos reservados.</summary>
    public class ReposicionSalida
    {
        public string Destino { get; set; }
        public List<LineaPickingAlmacenDTO> Lineas { get; set; } = new List<LineaPickingAlmacenDTO>();
    }

    public enum EstadoTerminarSalida
    {
        Terminada,
        /// <summary>Quedan paradas sin coger ni dar por falta.</summary>
        SinTerminar,
        SinPermiso,
        NoExiste,
        TipoNoValido,
        /// <summary>Este tipo de salida todavía se cierra fuera de aquí (Nesto).</summary>
        NoSeTerminaAqui
    }

    public class ResultadoTerminarSalida
    {
        public EstadoTerminarSalida Estado { get; set; }
        /// <summary>El motivo cuando no se ha podido terminar.</summary>
        public string Mensaje { get; set; }
        public ResultadoTerminarSalidaDTO Salida { get; set; }
    }

    /// <summary>
    /// NestoAPI#556: una estrategia de salida de mercancía, espejo de <see cref="IOrigenRecepcion"/>. Cada tipo dice qué
    /// hay por sacar, de dónde sale cada cosa, quién puede terminarlo y qué se hace al terminar; leer, casar lo leído,
    /// faltas y deshacer son comunes (<see cref="ServicioSalidas"/>).
    /// </summary>
    public interface IOrigenSalida
    {
        /// <summary>PICK, REPO… (4 letras, como TipoOrigen de los escaneos).</summary>
        string Tipo { get; }
        /// <summary>False mientras el cierre de este tipo siga en otro sitio (se puede leer y casar, no terminar).</summary>
        bool SeTerminaDesdeAqui { get; }
        bool PuedeTerminar(IPrincipal usuario, string empresa);
        Task<List<RecogidaPendienteDTO>> LeerPendientes(string empresa, string almacen);
        /// <summary>Null si no hay nada que sacar con ese número.</summary>
        Task<RecorridoSalida> LeerRecorrido(string empresa, int numero);
        /// <summary>Lo que hace este tipo al terminar, con la salida ya validada por el núcleo (todo resuelto).</summary>
        Task<ResultadoTerminarSalidaDTO> Terminar(string empresa, int numero, EstadoPickingDTO estado, IPrincipal usuario);
    }

    public interface IServicioSalidas
    {
        Task<List<RecogidaPendienteDTO>> LeerPendientes(string empresa, string almacen);
        Task<RecogidaAlmacenDTO> LeerRecogida(string empresa, string tipo, int numero);
        Task<ResultadoTerminarSalida> Terminar(string empresa, string tipo, int numero, IPrincipal usuario);
    }

    /// <summary>
    /// NestoAPI#556: el núcleo común de «sacar mercancía» (recoger un picking, sacar una reposición). El mozo lee y da por
    /// falta lo que no está; aquí se monta el recorrido con lo hecho, se casa lo leído con lo esperado (igual para todos
    /// los tipos) y, al terminar, se comprueba que no quede nada sin resolver y que el usuario puede, y se delega en la
    /// estrategia de su tipo. Lo usan Ariadna y, cuando haga falta, Nesto.
    /// </summary>
    public class ServicioSalidas : IServicioSalidas
    {
        private readonly Dictionary<string, IOrigenSalida> origenes;

        public ServicioSalidas(IEnumerable<IOrigenSalida> origenes)
        {
            this.origenes = (origenes ?? Enumerable.Empty<IOrigenSalida>())
                .ToDictionary(o => o.Tipo.Trim().ToUpperInvariant(), o => o);
        }

        private IOrigenSalida Origen(string tipo)
        {
            string limpio = tipo?.Trim().ToUpperInvariant();
            return limpio != null && origenes.TryGetValue(limpio, out IOrigenSalida origen) ? origen : null;
        }

        public async Task<List<RecogidaPendienteDTO>> LeerPendientes(string empresa, string almacen)
        {
            var pendientes = new List<RecogidaPendienteDTO>();
            foreach (IOrigenSalida origen in origenes.Values)
            {
                pendientes.AddRange(await origen.LeerPendientes(empresa, almacen).ConfigureAwait(false) ?? new List<RecogidaPendienteDTO>());
            }
            return pendientes;
        }

        public async Task<RecogidaAlmacenDTO> LeerRecogida(string empresa, string tipo, int numero)
        {
            IOrigenSalida origen = Origen(tipo);
            if (origen == null)
            {
                return null;
            }
            RecorridoSalida recorrido = await LeerRecorridoOrdenado(origen, empresa, numero).ConfigureAwait(false);
            return recorrido == null
                ? null
                : ServicioPreparacionAlmacen.MontarRecogida(empresa, origen.Tipo, numero, recorrido.Destino, recorrido.Lineas, recorrido.Lecturas);
        }

        public async Task<ResultadoTerminarSalida> Terminar(string empresa, string tipo, int numero, IPrincipal usuario)
        {
            IOrigenSalida origen = Origen(tipo);
            if (origen == null)
            {
                return new ResultadoTerminarSalida
                {
                    Estado = EstadoTerminarSalida.TipoNoValido,
                    Mensaje = $"El tipo de salida tiene que ser {string.Join(" o ", origenes.Keys)}."
                };
            }
            if (!origen.SeTerminaDesdeAqui)
            {
                return new ResultadoTerminarSalida
                {
                    Estado = EstadoTerminarSalida.NoSeTerminaAqui,
                    Mensaje = "Esta salida todavía se cierra en Nesto: lo leído queda guardado y la oficina lo ve allí."
                };
            }
            if (!origen.PuedeTerminar(usuario, empresa))
            {
                return new ResultadoTerminarSalida { Estado = EstadoTerminarSalida.SinPermiso, Mensaje = EscrituraSoloAlmacenAttribute.MENSAJE_SIN_PERMISO };
            }
            RecorridoSalida recorrido = await LeerRecorridoOrdenado(origen, empresa, numero).ConfigureAwait(false);
            if (recorrido == null)
            {
                return new ResultadoTerminarSalida { Estado = EstadoTerminarSalida.NoExiste, Mensaje = $"No hay nada que sacar en {origen.Tipo} {numero}." };
            }
            EstadoPickingDTO estado = ServicioPreparacionAlmacen.MontarEstadoPicking(empresa, numero, recorrido.Lineas, recorrido.Lecturas);
            if (!estado.Terminado)
            {
                int quedan = estado.Productos.Where(p => !p.Ajeno).Sum(p => Math.Max(0, p.Esperado - p.Leido - p.Faltas));
                return new ResultadoTerminarSalida
                {
                    Estado = EstadoTerminarSalida.SinTerminar,
                    Mensaje = $"Quedan {quedan} unidades sin coger ni dar por falta."
                };
            }
            return new ResultadoTerminarSalida
            {
                Estado = EstadoTerminarSalida.Terminada,
                Salida = await origen.Terminar(empresa, numero, estado, usuario).ConfigureAwait(false)
            };
        }

        private static async Task<RecorridoSalida> LeerRecorridoOrdenado(IOrigenSalida origen, string empresa, int numero)
        {
            RecorridoSalida recorrido = await origen.LeerRecorrido(empresa, numero).ConfigureAwait(false);
            if (recorrido?.Lineas == null || recorrido.Lineas.Count == 0)
            {
                return null;
            }
            recorrido.Lineas = CasadorEscaneos.OrdenarRecorrido(recorrido.Lineas);
            recorrido.Lecturas = recorrido.Lecturas ?? new List<LecturaPickingAlmacen>();
            return recorrido;
        }

        /// <summary>El resumen común de una salida terminada (lo que se ha cogido y lo que se ha dado por falta).</summary>
        internal static ResultadoTerminarSalidaDTO Resumen(string tipo, int numero, EstadoPickingDTO estado, string mensaje)
        {
            return new ResultadoTerminarSalidaDTO
            {
                Tipo = tipo,
                Numero = numero,
                Terminada = estado.Terminado,
                Completa = estado.Completo,
                UnidadesEnFalta = estado.Productos.Sum(p => p.Faltas),
                Productos = estado.Productos,
                Mensaje = mensaje
            };
        }

        internal static bool EsDeAlmacenODireccion(IPrincipal usuario) => EscrituraSoloAlmacenAttribute.PuedeEscribir(usuario);
    }

    /// <summary>
    /// PICK: el picking de pedidos de venta. Solo Algete prepara pickings (299 en 30 días, ninguno en Reina ni
    /// Alcobendas), así que lo terminan Almacén o Dirección, como cualquier escritura de api/Almacen.
    /// Terminar no cambia el pedido: lo que se ha dado por falta queda apuntado (PreparacionEscaneos) y se dice;
    /// sacarlo del pedido antes del albarán sigue siendo cosa de la oficina (ver #556).
    /// </summary>
    public class OrigenSalidaPicking : IOrigenSalida
    {
        private readonly IRepositorioPreparacionAlmacen repositorio;

        public OrigenSalidaPicking(IRepositorioPreparacionAlmacen repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public string Tipo => CasadorEscaneos.ORIGEN_PICKING;

        public bool SeTerminaDesdeAqui => true;

        public bool PuedeTerminar(IPrincipal usuario, string empresa) => ServicioSalidas.EsDeAlmacenODireccion(usuario);

        public async Task<List<RecogidaPendienteDTO>> LeerPendientes(string empresa, string almacen)
        {
            return (await repositorio.LeerPickingsEnCurso(empresa, almacen).ConfigureAwait(false) ?? new List<PickingEnCursoDTO>())
                .Select(p => new RecogidaPendienteDTO
                {
                    Tipo = Tipo,
                    Numero = p.Picking,
                    Destino = CasadorEscaneos.DESTINO_PICKING,
                    Lineas = p.Lineas,
                    Pedidos = p.Pedidos,
                    Unidades = p.Unidades
                })
                .ToList();
        }

        public async Task<RecorridoSalida> LeerRecorrido(string empresa, int numero)
        {
            List<LineaPickingAlmacenDTO> lineas = await repositorio.LeerLineasPicking(empresa, numero).ConfigureAwait(false);
            if (lineas == null || lineas.Count == 0)
            {
                return null;
            }
            return new RecorridoSalida
            {
                Destino = CasadorEscaneos.DESTINO_PICKING,
                Lineas = lineas,
                Lecturas = await repositorio.LeerLecturasDelPicking(empresa, numero).ConfigureAwait(false)
            };
        }

        public Task<ResultadoTerminarSalidaDTO> Terminar(string empresa, int numero, EstadoPickingDTO estado, IPrincipal usuario)
        {
            int faltas = estado.Productos.Sum(p => p.Faltas);
            string mensaje = faltas == 0
                ? "Picking recogido entero. Llévalo a la mesa de packing."
                : $"Picking recogido con {(faltas == 1 ? "1 unidad" : $"{faltas} unidades")} en falta. Llévalo a la mesa de packing; " +
                  "la oficina tiene que quitar lo que falta de los pedidos antes de facturarlos.";
            return Task.FromResult(ServicioSalidas.Resumen(Tipo, numero, estado, mensaje));
        }
    }

    /// <summary>
    /// REPO: la salida de un traspaso de reposición desde el almacén de origen. Hoy el traspaso lo crea Nesto viejo:
    /// deja la salida en PreExtrProducto (diario Almacenes.DiarioSalidaRep del origen, «General» en Algete) y la entrada
    /// en el diario de entrada del destino; prdUbicarReposicion reserva los huecos (Ubicaciones en estado 4, enlazadas
    /// por NºOrdenRepo) y, una vez recogido, la salida se contabiliza (prdExtrProducto del diario de salida; en los
    /// datos, unos 11 minutos después de crearla). Aquí se lee y se casa con la misma pantalla que un picking.
    /// Terminar desde aquí está pendiente: con faltas hay que rebajar también la reserva en estado 4, y eso no se puede
    /// probar sin escribir en producción (#556).
    /// </summary>
    public class OrigenSalidaReposicion : IOrigenSalida
    {
        private readonly IRepositorioPreparacionAlmacen repositorio;

        public OrigenSalidaReposicion(IRepositorioPreparacionAlmacen repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public string Tipo => CasadorEscaneos.ORIGEN_REPOSICION;

        public bool SeTerminaDesdeAqui => false;

        public bool PuedeTerminar(IPrincipal usuario, string empresa) => ServicioSalidas.EsDeAlmacenODireccion(usuario);

        public async Task<List<RecogidaPendienteDTO>> LeerPendientes(string empresa, string almacen)
        {
            return (await repositorio.LeerReposicionesPorSalir(empresa, almacen).ConfigureAwait(false) ?? new List<ReposicionPorSalir>())
                .Select(r => new RecogidaPendienteDTO
                {
                    Tipo = Tipo,
                    Numero = r.Traspaso,
                    Destino = r.Destino,
                    Lineas = r.Lineas,
                    Unidades = r.Unidades
                })
                .ToList();
        }

        public async Task<RecorridoSalida> LeerRecorrido(string empresa, int numero)
        {
            ReposicionSalida salida = await repositorio.LeerReposicionSalida(empresa, numero).ConfigureAwait(false);
            if (salida?.Lineas == null || salida.Lineas.Count == 0)
            {
                return null;
            }
            return new RecorridoSalida
            {
                Destino = salida.Destino,
                Lineas = salida.Lineas,
                Lecturas = await repositorio.LeerLecturasDeSalida(empresa, Tipo, numero).ConfigureAwait(false)
            };
        }

        public Task<ResultadoTerminarSalidaDTO> Terminar(string empresa, int numero, EstadoPickingDTO estado, IPrincipal usuario)
        {
            throw new NotSupportedException("La salida de una reposición todavía se contabiliza en Nesto.");
        }
    }
}
