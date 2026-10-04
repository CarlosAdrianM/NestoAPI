using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infrastructure;
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

    /// <summary>Ariadna#6: cómo ha ido anular lo que un mozo ha leído en una salida.</summary>
    public enum EstadoAnularLecturas
    {
        Anuladas,
        SinPermiso,
        /// <summary>La salida ya se terminó: lo que faltaba ya se quitó del pedido y no se puede deshacer desde aquí.</summary>
        YaTerminada,
        NoValido
    }

    public class ResultadoAnularLecturas
    {
        public EstadoAnularLecturas Estado { get; set; }
        public string Mensaje { get; set; }
        /// <summary>Las lecturas en negativo añadidas (0 si ese mozo no tenía nada subido).</summary>
        public int Filas { get; set; }
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
        /// <summary>
        /// Lo que hace este tipo al terminar, con la salida ya validada por el núcleo (todo resuelto), escribiendo solo en
        /// <paramref name="tx"/>: el núcleo decide si se guarda o se deshace (ensayo).
        /// </summary>
        Task<ResultadoTerminarSalidaDTO> Terminar(string empresa, int numero, EstadoPickingDTO estado, IPrincipal usuario, ITransaccionSalida tx);
        /// <summary>Para el ensayo: cómo leer las filas que este tipo toca al terminar (se llama antes y después).</summary>
        Task<FotoSalida> PrepararFoto(ITransaccionSalida tx, string empresa, int numero);
    }

    /// <summary>Lee las filas implicadas en una salida, tal como están en ese momento de la transacción.</summary>
    public delegate Task<List<FilaEnsayoDTO>> FotoSalida();

    public interface IServicioSalidas
    {
        Task<List<RecogidaPendienteDTO>> LeerPendientes(string empresa, string almacen);
        Task<RecogidaAlmacenDTO> LeerRecogida(string empresa, string tipo, int numero);
        Task<ResultadoTerminarSalida> Terminar(string empresa, string tipo, int numero, IPrincipal usuario, bool ensayo = false);
        Task<ResultadoAnularLecturas> AnularLecturas(string empresa, string tipo, int numero, string usuarioLecturas, IPrincipal usuario);
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
        private readonly IRepositorioSalidas escrituras;
        private readonly IRepositorioAnulacionLecturas anulaciones;

        public ServicioSalidas(IEnumerable<IOrigenSalida> origenes, IRepositorioSalidas escrituras = null, IRepositorioAnulacionLecturas anulaciones = null)
        {
            this.origenes = (origenes ?? Enumerable.Empty<IOrigenSalida>())
                .ToDictionary(o => o.Tipo.Trim().ToUpperInvariant(), o => o);
            this.escrituras = escrituras;
            this.anulaciones = anulaciones;
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

        public async Task<ResultadoTerminarSalida> Terminar(string empresa, string tipo, int numero, IPrincipal usuario, bool ensayo = false)
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
            if (ensayo && !PuedeEnsayar(usuario))
            {
                return new ResultadoTerminarSalida { Estado = EstadoTerminarSalida.SinPermiso, Mensaje = MENSAJE_ENSAYO_SIN_PERMISO };
            }
            if (!ensayo && !origen.PuedeTerminar(usuario, empresa))
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
            if (escrituras == null)
            {
                throw new InvalidOperationException("ServicioSalidas sin dónde escribir: no puede terminar salidas.");
            }
            return new ResultadoTerminarSalida
            {
                Estado = EstadoTerminarSalida.Terminada,
                Salida = ensayo
                    ? await Ensayar(origen, empresa, numero, estado, usuario).ConfigureAwait(false)
                    : await escrituras.EnTransaccion(tx => TerminarYApuntar(origen, empresa, numero, estado, usuario, tx), false).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// Lo que hace el tipo al terminar y, en la misma transacción, la marca de terminada (Ariadna#6). Si ya estaba
        /// terminada no se hace nada más: repetirlo quitaría otra vez las faltas, ahora de lo que sí se ha cogido (Ariadna
        /// termina antes de empaquetar, y el picking pudo terminarse en otra PDA o en otra sesión).
        /// </summary>
        private static async Task<ResultadoTerminarSalidaDTO> TerminarYApuntar(IOrigenSalida origen, string empresa, int numero, EstadoPickingDTO estado,
            IPrincipal usuario, ITransaccionSalida tx)
        {
            if (await tx.EstaTerminada(empresa, origen.Tipo, numero).ConfigureAwait(false))
            {
                string documento = origen.Tipo == CasadorEscaneos.ORIGEN_PICKING ? $"El picking {numero} ya estaba terminado" : $"{origen.Tipo} {numero} ya estaba terminada";
                return Resumen(origen.Tipo, numero, estado, $"{documento}: no se ha vuelto a tocar nada.");
            }
            ResultadoTerminarSalidaDTO hecho = await origen.Terminar(empresa, numero, estado, usuario, tx).ConfigureAwait(false);
            await tx.ApuntarTerminada(empresa, origen.Tipo, numero, NombreUsuario(usuario)).ConfigureAwait(false);
            return hecho;
        }

        /// <summary>
        /// Ariadna#6: descarta lo que un mozo ha leído en una salida (una prueba olvidada en la cola de su PDA). Siempre TODO
        /// lo de ese mozo en esa salida, nunca lecturas sueltas (un «Deshacer» es la misma lectura en negativo: quitar una
        /// sola podría dejar una falta sin su deshacer). Solo Admin o Dirección, y nunca en una salida ya terminada.
        /// </summary>
        public async Task<ResultadoAnularLecturas> AnularLecturas(string empresa, string tipo, int numero, string usuarioLecturas, IPrincipal usuario)
        {
            IOrigenSalida origen = Origen(tipo);
            if (origen == null)
            {
                return new ResultadoAnularLecturas
                {
                    Estado = EstadoAnularLecturas.NoValido,
                    Mensaje = $"El tipo de salida tiene que ser {string.Join(" o ", origenes.Keys)}."
                };
            }
            string mozo = usuarioLecturas?.Trim();
            if (string.IsNullOrEmpty(mozo))
            {
                return new ResultadoAnularLecturas { Estado = EstadoAnularLecturas.NoValido, Mensaje = "Falta de qué mozo son las lecturas." };
            }
            if (!PuedeEnsayar(usuario))
            {
                return new ResultadoAnularLecturas { Estado = EstadoAnularLecturas.SinPermiso, Mensaje = MENSAJE_ANULAR_SIN_PERMISO };
            }
            if (anulaciones == null)
            {
                throw new InvalidOperationException("ServicioSalidas sin dónde anular: no puede anular lecturas.");
            }
            int? filas = await anulaciones.Anular(empresa, origen.Tipo, numero, mozo, NombreUsuario(usuario)).ConfigureAwait(false);
            if (filas == null)
            {
                return new ResultadoAnularLecturas
                {
                    Estado = EstadoAnularLecturas.YaTerminada,
                    Mensaje = $"{origen.Tipo} {numero} ya está terminada: lo que faltaba ya se quitó del pedido y las lecturas de {mozo} no se pueden anular desde aquí."
                };
            }
            return new ResultadoAnularLecturas
            {
                Estado = EstadoAnularLecturas.Anuladas,
                Filas = filas.Value,
                Mensaje = filas.Value == 0
                    ? $"{mozo} no tenía nada subido en {origen.Tipo} {numero}."
                    : $"Anuladas las lecturas de {mozo} en {origen.Tipo} {numero}: lo suyo queda a cero, como si lo hubiera deshecho."
            };
        }

        public const string MENSAJE_ANULAR_SIN_PERMISO = "Descartar lo leído por un mozo solo lo pueden hacer Admin o Dirección.";

        public const string MENSAJE_ENSAYO_SIN_PERMISO = "El ensayo solo lo pueden lanzar Admin o Dirección.";

        private const string ROL_ADMIN = "Admin";

        /// <summary>Admin (rol de Identity) o Dirección (grupo de Windows): los que pueden ensayar en producción.</summary>
        internal static bool PuedeEnsayar(IPrincipal usuario)
        {
            return usuario != null &&
                (usuario.IsInRoleSinDominio(ROL_ADMIN) || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION));
        }

        /// <summary>
        /// El ensayo: EXACTAMENTE lo mismo que terminar de verdad (C#, triggers, procedimientos y restricciones), dentro de
        /// una transacción que se deshace siempre, con las filas implicadas antes y después. Si algo falla, no lanza:
        /// devuelve el error real para verlo.
        /// </summary>
        private async Task<ResultadoTerminarSalidaDTO> Ensayar(IOrigenSalida origen, string empresa, int numero, EstadoPickingDTO estado, IPrincipal usuario)
        {
            List<FilaEnsayoDTO> antes = null;
            List<FilaEnsayoDTO> despues = null;
            try
            {
                ResultadoTerminarSalidaDTO salida = await escrituras.EnTransaccion(async tx =>
                {
                    FotoSalida foto = await origen.PrepararFoto(tx, empresa, numero).ConfigureAwait(false);
                    antes = await foto().ConfigureAwait(false);
                    ResultadoTerminarSalidaDTO hecho = await TerminarYApuntar(origen, empresa, numero, estado, usuario, tx).ConfigureAwait(false);
                    despues = await foto().ConfigureAwait(false);
                    return hecho;
                }, true).ConfigureAwait(false);
                salida.Ensayo = true;
                salida.FilasAntes = antes;
                salida.FilasDespues = despues;
                salida.Mensaje = "ENSAYO (no se ha guardado nada): " + salida.Mensaje;
                return salida;
            }
            catch (Exception ex)
            {
                return new ResultadoTerminarSalidaDTO
                {
                    Tipo = origen.Tipo,
                    Numero = numero,
                    Terminada = false,
                    Ensayo = true,
                    FilasAntes = antes,
                    ErrorEnsayo = MensajeCompleto(ex),
                    Mensaje = "ENSAYO: ha fallado y no se ha guardado nada. " + MensajeCompleto(ex)
                };
            }
        }

        /// <summary>El mensaje con los de dentro (el del procedimiento suele ir en la excepción interna).</summary>
        internal static string MensajeCompleto(Exception ex)
        {
            var mensajes = new List<string>();
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (!string.IsNullOrWhiteSpace(e.Message) && !mensajes.Contains(e.Message))
                {
                    mensajes.Add(e.Message);
                }
            }
            return string.Join(" → ", mensajes);
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

        /// <summary>El usuario del token para las columnas de auditoría.</summary>
        internal static string NombreUsuario(IPrincipal usuario) => usuario?.Identity?.Name;

        internal static string Unidades(int cantidad) => cantidad == 1 ? "1 unidad" : $"{cantidad} unidades";

        /// <summary>«004002012» → «004/002/012», como lo ve el mozo.</summary>
        internal static string Hueco(string hueco) => hueco != null && hueco.Length == 9
            ? $"{hueco.Substring(0, 3)}/{hueco.Substring(3, 3)}/{hueco.Substring(6, 3)}"
            : hueco;
    }

    /// <summary>
    /// PICK: el picking de pedidos de venta. Solo Algete prepara pickings (299 en 30 días, ninguno en Reina ni
    /// Alcobendas), así que lo terminan Almacén o Dirección, como cualquier escritura de api/Almacen.
    /// Al terminar, lo que se ha dado por falta se quita: de su reserva (pasa a «pendiente de ubicar»: si no estaba en el
    /// hueco, devolverlo al hueco perpetuaría el error) y del pedido (lo que falta queda en una línea pendiente para otra
    /// entrega, como deja GeneradorPendientes lo que no tiene stock al sacar el picking). Así el albarán ya no lo lleva.
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

        public async Task<FotoSalida> PrepararFoto(ITransaccionSalida tx, string empresa, int numero)
        {
            // Los pedidos y productos se fijan antes de tocar nada: una línea que se queda sin picking sigue saliendo después
            List<int> pedidos = await tx.PedidosDelPicking(empresa, numero).ConfigureAwait(false);
            List<string> productos = (await tx.LeerPiezasPicking(empresa, numero).ConfigureAwait(false))
                .Select(p => p.Producto).Distinct().ToList();
            DateTime desde = (await tx.AhoraEnBaseDeDatos().ConfigureAwait(false)).AddSeconds(-1);
            return () => tx.FotoPicking(empresa, pedidos, productos, desde);
        }

        public async Task<ResultadoTerminarSalidaDTO> Terminar(string empresa, int numero, EstadoPickingDTO estado, IPrincipal usuario, ITransaccionSalida tx)
        {
            List<FaltaSalida> faltas = PlanificadorFaltasSalida.AgruparFaltas(await tx.LeerFaltas(empresa, Tipo, numero).ConfigureAwait(false));
            int unidades = faltas.Sum(f => f.Cantidad);
            if (unidades == 0)
            {
                return ServicioSalidas.Resumen(Tipo, numero, estado, "Picking recogido entero. Llévalo a la mesa de packing.");
            }

            List<RecorteSalida> recortes = PlanificadorFaltasSalida.Repartir(faltas, await tx.LeerPiezasPicking(empresa, numero).ConfigureAwait(false));
            List<RecorteSalida> solos = recortes.Where(r => !r.Pieza.QuitarAMano).ToList();
            List<RecorteSalida> aMano = recortes.Where(r => r.Pieza.QuitarAMano).ToList();
            string nombreUsuario = ServicioSalidas.NombreUsuario(usuario);
            var cambios = new List<string>();

            // Primero las reservas: la línea se parte con lo que le queda reservado
            foreach (RecorteSalida recorte in solos.Where(r => r.Pieza.Ubicacion != null))
            {
                await tx.QuitarDeLaReserva(recorte.Pieza, recorte.Cantidad, nombreUsuario).ConfigureAwait(false);
                cambios.Add($"{ServicioSalidas.Unidades(recorte.Cantidad)} de {recorte.Pieza.Producto} " +
                    $"{(recorte.Pieza.Hueco == null ? "sin hueco" : "del hueco " + ServicioSalidas.Hueco(recorte.Pieza.Hueco))} pasa a «pendiente de ubicar».");
            }
            foreach (IGrouping<int, RecorteSalida> linea in solos.GroupBy(r => r.Pieza.Linea))
            {
                int cantidad = linea.Sum(r => r.Cantidad);
                await tx.SacarDelPedido(linea.Key, cantidad).ConfigureAwait(false);
                RecorteSalida primero = linea.First();
                cambios.Add($"Pedido {primero.Pieza.Pedido}: {ServicioSalidas.Unidades(cantidad)} de {primero.Pieza.Producto} queda pendiente para otra entrega.");
            }
            cambios.AddRange(await SacarLoQueSeQuedaSinProducto(empresa, numero, solos.Select(r => r.Pieza.Pedido).Distinct().ToList(), tx).ConfigureAwait(false));
            foreach (RecorteSalida recorte in aMano)
            {
                cambios.Add($"Pedido {recorte.Pieza.Pedido}: {ServicioSalidas.Unidades(recorte.Cantidad)} de {recorte.Pieza.Producto} " +
                    "es de una línea con producto «en carpeta»: hay que quitarla a mano.");
            }

            string mensaje = $"Picking recogido con {ServicioSalidas.Unidades(unidades)} en falta: ";
            if (solos.Any())
            {
                mensaje += "ya se ha quitado de sus pedidos (queda pendiente para otra entrega) y lo que no estaba en su hueco pasa a «pendiente de ubicar». ";
            }
            if (aMano.Any())
            {
                mensaje += $"Ojo: {ServicioSalidas.Unidades(aMano.Sum(r => r.Cantidad))} es de una línea con producto «en carpeta» y la oficina tiene que quitarla a mano. ";
            }
            mensaje += "Llévalo a la mesa de packing.";

            ResultadoTerminarSalidaDTO resumen = ServicioSalidas.Resumen(Tipo, numero, estado, mensaje);
            resumen.Cambios = cambios;
            return resumen;
        }

        /// <summary>
        /// Regla aprobada por Carlos (03/10/26): si a un pedido, después de quitar las faltas, no le queda NINGUNA línea de
        /// producto en el picking, sus líneas sin producto (portes, cuentas…) tampoco salen: pasan enteras a pendiente y sin
        /// picking (por el mismo SacarDelPedido que las faltas) y esperan con el resto del pedido. Si le queda algún
        /// producto, se quedan. Solo se miran los pedidos a los que se ha quitado algo.
        /// </summary>
        private static async Task<List<string>> SacarLoQueSeQuedaSinProducto(string empresa, int numero, IReadOnlyCollection<int> pedidos, ITransaccionSalida tx)
        {
            var cambios = new List<string>();
            if (pedidos.Count == 0)
            {
                return cambios;
            }
            List<LineaEnPickingSalida> siguen = await tx.LineasQueSiguenEnElPicking(empresa, numero, pedidos).ConfigureAwait(false)
                ?? new List<LineaEnPickingSalida>();
            foreach (IGrouping<int, LineaEnPickingSalida> pedido in siguen.GroupBy(l => l.Pedido).OrderBy(g => g.Key))
            {
                if (pedido.Any(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO))
                {
                    continue;
                }
                foreach (LineaEnPickingSalida linea in pedido)
                {
                    await tx.SacarDelPedido(linea.Linea, linea.Cantidad).ConfigureAwait(false);
                }
                cambios.Add($"Pedido {pedido.Key}: los portes esperan con el resto ({string.Join(", ", pedido.Select(l => l.Producto?.Trim()))}): " +
                    "no le queda ningún producto en este picking.");
            }
            return cambios;
        }
    }

    /// <summary>
    /// REPO: la salida de un traspaso de reposición desde el almacén de origen. Hoy el traspaso lo crea Nesto viejo:
    /// deja la salida en PreExtrProducto (diario Almacenes.DiarioSalidaRep del origen, «General» en Algete) y la entrada
    /// en el diario de entrada del destino, y quita ya la mercancía de los huecos dejando su registro en Ubicaciones
    /// (estado -4, NºOrdenRepo = la fila de la salida). Una vez recogida, la salida se contabiliza (prdExtrProducto del
    /// diario de salida; en los datos, unos 11 minutos después de crearla). Eso es terminar: lo que se ha dado por falta
    /// sale antes de la salida, de la entrada del destino y del registro del hueco, y pasa a «pendiente de ubicar».
    /// En 60 días, cada vez que se contabilizó la salida había un solo traspaso en el diario: si hay otro, no se termina.
    /// </summary>
    public class OrigenSalidaReposicion : IOrigenSalida
    {
        private readonly IRepositorioPreparacionAlmacen repositorio;

        public OrigenSalidaReposicion(IRepositorioPreparacionAlmacen repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public string Tipo => CasadorEscaneos.ORIGEN_REPOSICION;

        public bool SeTerminaDesdeAqui => true;

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

        public async Task<FotoSalida> PrepararFoto(ITransaccionSalida tx, string empresa, int numero)
        {
            List<string> productos = (await tx.LeerPiezasReposicion(empresa, numero).ConfigureAwait(false))
                .Select(p => p.Producto).Distinct().ToList();
            DateTime desde = (await tx.AhoraEnBaseDeDatos().ConfigureAwait(false)).AddSeconds(-1);
            return () => tx.FotoReposicion(empresa, numero, productos, desde);
        }

        public async Task<ResultadoTerminarSalidaDTO> Terminar(string empresa, int numero, EstadoPickingDTO estado, IPrincipal usuario, ITransaccionSalida tx)
        {
            DiarioSalidaReposicion diario = await tx.DiarioDeSalida(empresa, numero).ConfigureAwait(false);
            if (diario == null)
            {
                throw new NestoBusinessException($"El traspaso {numero} ya no tiene salida pendiente: ¿ya se ha sacado?");
            }
            string nombreUsuario = ServicioSalidas.NombreUsuario(usuario);
            List<FaltaSalida> faltas = PlanificadorFaltasSalida.AgruparFaltas(await tx.LeerFaltas(empresa, Tipo, numero).ConfigureAwait(false));
            int unidades = faltas.Sum(f => f.Cantidad);
            var cambios = new List<string>();
            bool saleAlgo = true;
            if (unidades > 0)
            {
                List<PiezaSalida> piezas = await tx.LeerPiezasReposicion(empresa, numero).ConfigureAwait(false);
                List<RecorteSalida> recortes = PlanificadorFaltasSalida.Repartir(faltas, piezas);
                foreach (RecorteSalida recorte in recortes)
                {
                    await tx.QuitarDeLaReposicion(empresa, numero, recorte.Pieza, recorte.Cantidad, nombreUsuario).ConfigureAwait(false);
                    cambios.Add($"{ServicioSalidas.Unidades(recorte.Cantidad)} de {recorte.Pieza.Producto} no sale" +
                        $"{(recorte.Pieza.Hueco == null ? string.Empty : " del hueco " + ServicioSalidas.Hueco(recorte.Pieza.Hueco))}: " +
                        "se quita también de la entrada y pasa a «pendiente de ubicar».");
                }
                saleAlgo = piezas.Sum(p => p.Cantidad) > recortes.Sum(r => r.Cantidad);
            }

            if (!saleAlgo)
            {
                ResultadoTerminarSalidaDTO nada = ServicioSalidas.Resumen(Tipo, numero, estado,
                    $"No ha salido nada del traspaso {numero}: todo se ha dado por falta, se ha quitado de la salida y de la entrada " +
                    "y está en «pendiente de ubicar».");
                nada.Cambios = cambios;
                return nada;
            }

            // NestoAPI#553: ALG→REI y ALG→ALC comparten el diario «General» y prdExtrProducto lo contabiliza entero. Se apartan
            // las demás (como se hacía a mano con «RepoEscond») para que solo salga esta y se puede ir a las dos tiendas el mismo día
            List<int> apartadas = await tx.ApartarOtros(empresa, diario.Diario, numero).ConfigureAwait(false) ?? new List<int>();
            await tx.Contabilizar(empresa, diario.Diario, nombreUsuario).ConfigureAwait(false);
            await tx.DevolverApartadas(empresa, diario.Diario, apartadas).ConfigureAwait(false);
            cambios.Add($"Contabilizada la salida de la reposición {numero} (diario {diario.Diario} de {diario.Almacen}).");
            string mensaje = $"Reposición {numero} sacada de {diario.Almacen} hacia {diario.Destino}.";
            if (unidades > 0)
            {
                mensaje += $" {ServicioSalidas.Unidades(unidades)} en falta: no salen, se quitan también de la entrada y pasan a «pendiente de ubicar».";
            }
            ResultadoTerminarSalidaDTO resumen = ServicioSalidas.Resumen(Tipo, numero, estado, mensaje);
            resumen.Cambios = cambios;
            return resumen;
        }
    }
}
