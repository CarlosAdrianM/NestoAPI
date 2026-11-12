using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>C1 (Carlos 12/11/26): cuerpo de POST api/Almacen/Recogidas/{tipo}/{numero}/NoEsta.</summary>
    public class NoEstaSalidaDTO
    {
        public string Producto { get; set; }
        /// <summary>El hueco de la parada (PPP/FFF/CCC o PPPFFFCCC).</summary>
        public string Hueco { get; set; }
        /// <summary>Las unidades que no están (las que quedaban por coger en esa parada).</summary>
        public int Cantidad { get; set; }
    }

    /// <summary>Un hueco en el que ha quedado reservado lo que no estaba en el de la parada.</summary>
    public class HuecoReubicadoNoEstaDTO
    {
        /// <summary>«003/001/002», como se enseña.</summary>
        public string Ubicacion { get; set; }
        /// <summary>«003001002».</summary>
        public string Codigo { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>
    /// C1: lo que contesta el «No está» de una parada. Contrato para Ariadna (campos nuevos: la Ariadna publicada no llama a este
    /// endpoint y sigue como siempre). Con <see cref="Regularizado"/> false NO se ha tocado nada: la app da la falta como hasta ahora
    /// (escaneo FALTA), y <see cref="MotivoSinRegularizar"/> dice por qué.
    /// </summary>
    public class ResultadoNoEstaDTO
    {
        public string Tipo { get; set; }
        public int Numero { get; set; }
        public string Producto { get; set; }
        /// <summary>El hueco de la parada, «PPP/FFF/CCC».</summary>
        public string Hueco { get; set; }
        /// <summary>El hueco se ha dejado a cero en el inventario, contabilizado en el acto.</summary>
        public bool Regularizado { get; set; }
        public string MotivoSinRegularizar { get; set; }
        /// <summary>Lo que se ha quitado del stock del almacén (lo libre del hueco más lo reservado de esta salida que no estaba).</summary>
        public int UnidadesRegularizadas { get; set; }
        /// <summary>El traspaso del inventario (Inventarios.NºTraspaso) con el que se ha contabilizado.</summary>
        public int? TraspasoInventario { get; set; }
        /// <summary>Lo que ha quedado reservado en otros huecos: el mozo va a por ello (la recogida, al volver a leerla, ya lo trae).</summary>
        public int UnidadesReubicadas { get; set; }
        /// <summary>El primer hueco al que ir («PPP/FFF/CCC»). Null si no hay ninguno.</summary>
        public string HuecoPropuesto { get; set; }
        public List<HuecoReubicadoNoEstaDTO> Huecos { get; set; } = new List<HuecoReubicadoNoEstaDTO>();
        /// <summary>No queda stock calculado en ningún otro hueco: lo que falta es falta definitiva y ya se ha quitado de la salida.</summary>
        public bool SinMasHuecos { get; set; }
        public int UnidadesEnFaltaDefinitiva { get; set; }
        public bool Ensayo { get; set; }
        /// <summary>Para el mozo, tal cual.</summary>
        public string Mensaje { get; set; }
        public List<string> Cambios { get; set; } = new List<string>();
    }

    public enum EstadoNoEsta
    {
        /// <summary>Hecho (o no hacía falta regularizar: ver <see cref="ResultadoNoEstaDTO.Regularizado"/>).</summary>
        Hecho,
        NoValido,
        SinPermiso,
        NoExiste,
        /// <summary>La salida ya está terminada, o la regularización ha fallado (no se ha tocado nada): 409 con el motivo.</summary>
        NoSePuede
    }

    public class ResultadoNoEsta
    {
        public EstadoNoEsta Estado { get; set; }
        public string Mensaje { get; set; }
        public ResultadoNoEstaDTO Resultado { get; set; }
        /// <summary>Las faltas definitivas, para el aviso (A) una vez guardado.</summary>
        internal List<FaltaPickingPedido> FaltasPorAvisar { get; set; } = new List<FaltaPickingPedido>();
        internal string Almacen { get; set; }
    }

    /// <summary>
    /// La reserva de esta salida en el hueco de la parada: en el picking, el estado 3 de una línea de pedido; en la reposición
    /// (NestoAPI#858), el estado 4 del traspaso (NºTraspasoRepo), con su línea de entrada (NºOrdenRepo).
    /// </summary>
    public class ReservaPropiaNoEsta
    {
        /// <summary>Ubicaciones.NºOrden de la reserva.</summary>
        public int Ubicacion { get; set; }
        /// <summary>La línea del pedido (picking). 0 en la reposición.</summary>
        public int Linea { get; set; }
        /// <summary>El pedido (picking) o el traspaso (reposición).</summary>
        public int Pedido { get; set; }
        /// <summary>NestoAPI#858, reposición: Ubicaciones.NºOrdenRepo de la reserva (la línea de ENTRADA del traspaso). Null en el picking.</summary>
        public int? LineaEntrada { get; set; }
        public string EmpresaLinea { get; set; }
        public string Almacen { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public bool EsMuestra { get; set; }
        public bool EsNotaEntrega { get; set; }
    }

    /// <summary>Lo de un producto para su fila de inventario.</summary>
    public class ProductoNoEsta
    {
        public string Numero { get; set; }
        public string Descripcion { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public string Familia { get; set; }
        public bool Ficticio { get; set; }
        public bool Ubicar { get; set; }
    }

    /// <summary>Filas de Inventarios pendientes (Estado 1, sin NºTraspaso) en el almacén: del producto (cualquier usuario) y del usuario técnico.</summary>
    public class InventarioPendienteNoEsta
    {
        public int DelProducto { get; set; }
        public int DelUsuario { get; set; }
    }

    /// <summary>
    /// C1: lo que lee y escribe el «No está», todo en UNA transacción (la de <see cref="IRepositorioNoEsta"/>). Lo de quitar lo que
    /// falta de la salida es lo mismo que al terminar (<see cref="ITransaccionSalida"/>).
    /// </summary>
    public interface ITransaccionNoEsta : ITransaccionSalida
    {
        /// <summary>sp_getapplock exclusivo de la transacción (los del inventario: InventarioRellenar:…). 15 s y error claro.</summary>
        Task Bloquear(string recurso);
        Task<bool> ControlUbicaciones(string empresa, string almacen);
        Task<ProductoNoEsta> LeerProducto(string empresa, string producto);
        Task<InventarioPendienteNoEsta> InventarioPendiente(string empresa, string almacen, string producto, string usuarioTecnico);
        /// <summary>Lo libre (Ubicaciones estado 0) del producto en el hueco, de todas las empresas (como el inventario).</summary>
        Task<int> LibreEnHueco(string almacen, string producto, string hueco);
        /// <summary>ExtractoProducto del producto en el almacén (empresas 1 y 3). Solo se lee.</summary>
        Task<int> StockAlmacen(string almacen, string producto);
        /// <summary>La fila pendiente del hueco (StockReal 0) con el insert de siempre del inventario.</summary>
        Task InsertarFilaInventario(Inventario fila);
        /// <summary>prdContabilizarInventario por su ÚNICO call site (RepositorioContabilizarInventarioSql), dentro de esta transacción.</summary>
        Task<int> Contabilizar(string empresa, string usuario, string almacen);
        Task<int?> LeerTraspasoInventario(string empresa, string almacen, string usuario, DateTime fecha);
        /// <summary>Los otros huecos con libre del producto en el almacén (sin ordenar).</summary>
        Task<List<HuecoAlternativoDTO>> LibresEnOtrosHuecos(string empresa, string almacen, string producto, string huecoOrigen);
        /// <summary>Las filas libres del hueco, para reservar de ellas.</summary>
        Task<List<LibreEnHueco>> LibresEnHueco(string empresa, string almacen, string producto, string hueco);
        Task<string> AlmacenPicking(string empresa, int picking);
        /// <summary>Las reservas (estado 3) de las líneas de ese picking con ese producto en ese hueco, el pedido más nuevo primero.</summary>
        Task<List<ReservaPropiaNoEsta>> ReservasPicking(string empresa, int picking, string producto, string hueco);
        /// <summary>
        /// Esas unidades de la reserva (picking: estado 3; reposición: estado 4) vuelven a ser libres EN SU HUECO (estado 0), para
        /// que el inventario las quite. Común a los dos orígenes.
        /// </summary>
        Task SoltarReservaAlHueco(int ubicacion, int cantidad, string usuario);
        /// <summary>Reserva de la fila libre para la línea (lo de CambiarHueco, Ariadna#12).</summary>
        Task ReservarDeLaLibre(int libre, int cantidad, int linea, int pedido, string empresaLinea, string usuario);
        Task ApuntarRastro(Modificacion rastro);

        // ---- NestoAPI#858: la reposición (REPO) ----

        /// <summary>El almacén de origen del traspaso (el de su salida pendiente). Null si ya no tiene salida pendiente.</summary>
        Task<string> AlmacenReposicion(string empresa, int traspaso);
        /// <summary>Las reservas (estado 4, NºTraspasoRepo) del traspaso con ese producto en ese hueco, la más nueva primero. Las -4 (Nesto viejo) no.</summary>
        Task<List<ReservaPropiaNoEsta>> ReservasReposicion(string empresa, int traspaso, string producto, string hueco);
        /// <summary>De la fila libre, una reserva del traspaso (estado 4, con su línea de entrada), como CambiarHuecoReservaReposicion.</summary>
        Task ReservarDeLaLibreParaElTraspaso(int libre, int cantidad, int? lineaEntrada, int traspaso, string usuario);
        /// <summary>Las unidades que quedan en la salida del traspaso (todas sus líneas de salida en PreExtrProducto).</summary>
        Task<int> UnidadesPorSalir(string empresa, int traspaso);
        /// <summary>
        /// La falta definitiva de una reposición: menos en la salida del origen y menos en la entrada del destino (PreExtrProducto),
        /// SIN tocar Ubicaciones (la reserva ya se ha soltado al hueco y el inventario la ha quitado): nada de «pendiente de ubicar».
        /// </summary>
        Task QuitarDelTraspaso(string empresa, int traspaso, string producto, int cantidad);
    }

    public interface IRepositorioNoEsta
    {
        /// <summary>Hace el trabajo en una transacción; con <paramref name="deshacerSiempre"/> (ensayo) la deshace siempre. Si algo lanza, la deshace.</summary>
        Task<ResultadoNoEsta> EnTransaccion(Func<ITransaccionNoEsta, Task<ResultadoNoEsta>> trabajo, bool deshacerSiempre);
    }

    /// <summary>
    /// C1: lo propio de cada tipo de salida en el «No está», y SOLO eso (Carlos 12/11/26: «lo máximo común posible»): (a) cuál es la
    /// reserva propia y cómo se suelta al hueco, (b) cómo se reserva en el siguiente hueco y (c) qué es la falta definitiva. Bloqueos,
    /// inventario, contabilización, cuadre, rastro, ELMAH, respuesta y ensayo son del núcleo (<see cref="ServicioNoEstaSalida"/>).
    /// PICK (<see cref="OrigenNoEstaPicking"/>) y, desde NestoAPI#858, REPO (<see cref="OrigenNoEstaReposicion"/>): son los dos únicos
    /// tipos de salida (<see cref="CasadorEscaneos.NormalizarTipoOrigen"/>).
    /// </summary>
    public interface IOrigenNoEsta
    {
        string Tipo { get; }
        Task<string> LeerAlmacen(ITransaccionNoEsta tx, string empresa, int numero);
        /// <summary>Lo reservado por esta salida en ese hueco (lo que se suelta, como mucho lo que no está), en el orden en que se suelta.</summary>
        Task<List<ReservaPropiaNoEsta>> ReservasPropias(ITransaccionNoEsta tx, string empresa, int numero, string producto, string hueco);
        Task SoltarAlHueco(ITransaccionNoEsta tx, ReservaPropiaNoEsta reserva, int cantidad, string usuario);
        Task Reservar(ITransaccionNoEsta tx, ReservaPropiaNoEsta reserva, LibreEnHueco libre, int cantidad, string usuario);
        /// <summary>
        /// Lo que no se ha podido reservar en ningún hueco sale de la salida (como una falta al terminar). Devuelve los cambios. Si en
        /// ese caso concreto no es seguro, lanza <see cref="NoEstaNoSeRegularizaException"/> ANTES de escribir: el núcleo contesta
        /// Regularizado = false y se deshace todo (la app da la falta como siempre).
        /// </summary>
        Task<List<string>> FaltaDefinitiva(ITransaccionNoEsta tx, string empresa, int numero, List<RecorteSalida> recortes, List<FaltaPickingPedido> avisar);
    }

    /// <summary>
    /// C1 (Carlos 12/11/26): «Es ir hueco a hueco buscando, hasta que en uno haya stock real o hasta que en ninguno quede ya stock
    /// calculado. En cada hueco que marquemos que no hay, se hace inventario del hueco para que no salga en otro picking.» El núcleo,
    /// común a cualquier salida, en UNA transacción:
    /// <list type="number">
    /// <item>Bloqueos del inventario (InventarioRellenar:{empresa}:{almacén} y el del hueco, los mismos que el rellenado y el «a cero»).</item>
    /// <item>Si alguien está inventariando el producto en el almacén (filas pendientes), NO se toca nada: Regularizado false.</item>
    /// <item>Lo reservado por ESTA salida en el hueco (como mucho lo que no está) vuelve a libre en el hueco; lo reservado para
    /// otras salidas no se toca.</item>
    /// <item>Fila de Inventarios del hueco (teórico = lo libre, contado 0) con un usuario técnico propio y prdContabilizarInventario en
    /// el acto (solo contabiliza las filas de ese usuario en ese almacén): ExtractoProducto y Ubicaciones por el SP. Se comprueba que no
    /// queda ninguna fila pendiente, que el hueco queda sin libre y que el extracto ha bajado justo eso; si no, se deshace todo.</item>
    /// <item>Lo soltado se reserva en el siguiente hueco con libre (orden del recorrido); lo que no cabe en ninguno es falta definitiva
    /// y sale de la salida como al terminar (A y B incluidos).</item>
    /// </list>
    /// NestoAPI#858 (Carlos 12/11/26): ENCENDIDO por defecto, también sin fila. Apagado de emergencia sin publicar: ParametrosUsuario
    /// empresa 1, usuario «(defecto)», clave <see cref="CLAVE"/> = «0» (o false/no/off/apagado). Ensayo: lo mismo, deshecho siempre.
    /// Cada «No está» regularizado deja su rastro en Modificaciones y una línea «[No está] …» en ELMAH para vigilarlo.
    /// </summary>
    public interface IServicioNoEstaSalida
    {
        Task<ResultadoNoEsta> MarcarNoEsta(string empresa, string tipo, int numero, NoEstaSalidaDTO peticion, IPrincipal usuario, bool ensayo = false);
    }

    public class ServicioNoEstaSalida : IServicioNoEstaSalida
    {
        public const string CLAVE = "Salidas.NoEstaRegulariza";
        /// <summary>El usuario de las filas de inventario del «No está»: el SP solo contabiliza las de este usuario (no las de nadie más).</summary>
        public const string USUARIO_INVENTARIO = "Ariadna (No está)";
        public const string TABLA_RASTRO = "Inventarios";
        public const string OPERACION = "No está: hueco a cero por inventario";

        private readonly Dictionary<string, IOrigenNoEsta> origenes;
        private readonly IRepositorioNoEsta repositorio;
        private readonly ILectorOrdenRecorrido ordenRecorrido;
        private readonly IAvisadorFaltasPicking avisadorFaltas;
        private readonly Func<bool> activo;
        private readonly Func<DateTime> ahora;

        public ServicioNoEstaSalida(IEnumerable<IOrigenNoEsta> origenes, IRepositorioNoEsta repositorio, ILectorOrdenRecorrido ordenRecorrido = null,
            IAvisadorFaltasPicking avisadorFaltas = null, Func<bool> activo = null, Func<DateTime> ahora = null)
        {
            this.origenes = (origenes ?? Enumerable.Empty<IOrigenNoEsta>()).ToDictionary(o => o.Tipo.Trim().ToUpperInvariant(), o => o);
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ordenRecorrido = ordenRecorrido;
            this.avisadorFaltas = avisadorFaltas;
            this.activo = activo ?? ActivoSegunConfiguracion;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>
        /// NestoAPI#858: el interruptor vive en ParametrosUsuario «(defecto)» de la empresa 1 (los flags de Web.config se pierden al
        /// publicar). Se lee en cada «No está» (sin caché: apagarlo vale al momento) y sin crear filas. Sin fila, encendido; si no se
        /// puede leer, encendido (lo demás va en la misma base de datos y, si falla, falla con 409 sin tocar nada).
        /// </summary>
        internal static bool ActivoSegunConfiguracion()
        {
            try
            {
                return Interpretar(NestoAPI.Controllers.ParametrosUsuarioController.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, CLAVE));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[No está] No se ha podido leer el interruptor {CLAVE}: se deja encendido. {ex.Message}", ex), USUARIO_INVENTARIO);
                return true;
            }
        }

        internal const string USUARIO_DEFECTO = "(defecto)";

        /// <summary>Encendido salvo que diga expresamente que no: «0», «false», «no», «off» o «apagado» (vacío o sin fila = encendido).</summary>
        internal static bool Interpretar(string valor)
        {
            string limpio = valor?.Trim().ToLowerInvariant();
            return !(limpio == "0" || limpio == "false" || limpio == "no" || limpio == "off" || limpio == "apagado");
        }

        public async Task<ResultadoNoEsta> MarcarNoEsta(string empresa, string tipo, int numero, NoEstaSalidaDTO peticion, IPrincipal usuario, bool ensayo = false)
        {
            string tipoSalida = CasadorEscaneos.NormalizarTipoOrigen(tipo);
            if (tipoSalida == null)
            {
                return Error(EstadoNoEsta.NoValido, $"El tipo de salida tiene que ser {CasadorEscaneos.ORIGEN_PICKING} o {CasadorEscaneos.ORIGEN_REPOSICION}.");
            }
            string producto = peticion?.Producto?.Trim();
            string hueco = PlanificadorFaltasSalida.NormalizarHueco(peticion?.Hueco);
            if (string.IsNullOrEmpty(producto) || producto.Length > 15)
            {
                return Error(EstadoNoEsta.NoValido, "Falta el producto.");
            }
            if (hueco == null)
            {
                return Error(EstadoNoEsta.NoValido, "Falta el hueco donde no estaba (PPP/FFF/CCC).");
            }
            if (peticion.Cantidad <= 0)
            {
                return Error(EstadoNoEsta.NoValido, "La cantidad tiene que ser mayor que cero.");
            }
            if (ensayo ? !ServicioSalidas.PuedeEnsayar(usuario) : !ServicioSalidas.EsDeAlmacenODireccion(usuario))
            {
                return Error(EstadoNoEsta.SinPermiso, ensayo ? ServicioSalidas.MENSAJE_ENSAYO_SIN_PERMISO : EscrituraSoloAlmacenAttribute.MENSAJE_SIN_PERMISO);
            }
            var base_ = new ResultadoNoEstaDTO { Tipo = tipoSalida, Numero = numero, Producto = producto, Hueco = ServicioSalidas.Hueco(hueco), Ensayo = ensayo };
            if (!origenes.TryGetValue(tipoSalida, out IOrigenNoEsta origen))
            {
                return SinRegularizar(base_, "En este tipo de salida el hueco todavía no se deja a cero: dé la falta como siempre.");
            }
            if (!activo())
            {
                return SinRegularizar(base_, $"Dejar el hueco a cero está apagado (parámetro {CLAVE} de «{USUARIO_DEFECTO}»): dé la falta como siempre.");
            }

            string quien = ServicioSalidas.NombreUsuario(usuario);
            ResultadoNoEsta resultado;
            try
            {
                resultado = await repositorio.EnTransaccion(tx => Hacer(origen, tx, empresa, numero, producto, hueco, peticion.Cantidad, quien, base_), ensayo)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[No está] {tipoSalida} {numero}, {producto} en {ServicioSalidas.Hueco(hueco)}: no se ha podido dejar el hueco a cero: " +
                    ServicioSalidas.MensajeCompleto(ex), ex), quien);
                return Error(EstadoNoEsta.NoSePuede, "No se ha podido dejar el hueco a cero en el inventario y no se ha tocado nada " +
                    $"({ServicioSalidas.MensajeCompleto(ex)}). Inténtelo otra vez o dé la falta como siempre.");
            }

            if (!ensayo && resultado?.Estado == EstadoNoEsta.Hecho && resultado.Resultado?.Regularizado == true)
            {
                // NestoAPI#858: va encendido sin haberse probado contra la base de datos real: cada uno, a la vista en ELMAH
                try
                {
                    ElmahHelper.Log(new Exception(LineaVigilancia(resultado.Resultado, resultado.Almacen)), quien);
                }
                catch (Exception)
                {
                    // Solo es el aviso para vigilarlo: el «No está» ya está guardado
                }
            }

            if (!ensayo && resultado.Estado == EstadoNoEsta.Hecho && resultado.FaltasPorAvisar.Count > 0 && avisadorFaltas != null)
            {
                try
                {
                    avisadorFaltas.Avisar(empresa, numero, resultado.Almacen, resultado.FaltasPorAvisar);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[No está] No se pudo lanzar el aviso de la falta de {numero}: {ex.Message}", ex), AvisadorFaltasPicking.USUARIO_AUDITORIA);
                }
            }
            return resultado;
        }

        private async Task<ResultadoNoEsta> Hacer(IOrigenNoEsta origen, ITransaccionNoEsta tx, string empresa, int numero, string producto, string hueco,
            int cantidad, string quien, ResultadoNoEstaDTO dto)
        {
            if (await tx.EstaTerminada(empresa, origen.Tipo, numero).ConfigureAwait(false))
            {
                return Error(EstadoNoEsta.NoSePuede, "Esta salida ya está terminada: no se ha tocado nada.");
            }
            string almacen = (await origen.LeerAlmacen(tx, empresa, numero).ConfigureAwait(false))?.Trim();
            if (string.IsNullOrEmpty(almacen))
            {
                return Error(EstadoNoEsta.NoExiste, $"No hay nada que sacar en {origen.Tipo} {numero}.");
            }
            // Las tiendas (sin control de ubicaciones) no cambian (requisito 6)
            if (!await tx.ControlUbicaciones(empresa, almacen).ConfigureAwait(false))
            {
                return SinRegularizar(dto, $"{almacen} no lleva ubicaciones: dé la falta como siempre.");
            }
            ProductoNoEsta ficha = await tx.LeerProducto(empresa, producto).ConfigureAwait(false);
            if (ficha == null)
            {
                return Error(EstadoNoEsta.NoValido, $"No existe el producto {producto}.");
            }
            if (ficha.Ficticio || !ficha.Ubicar)
            {
                return SinRegularizar(dto, $"El {producto} no se ubica: dé la falta como siempre.");
            }

            // Los bloqueos del inventario, en el orden del «a cero» (almacén y luego hueco): nadie rellena ni contabiliza entre medias
            await tx.Bloquear($"InventarioRellenar:{empresa}:{almacen}").ConfigureAwait(false);
            await tx.Bloquear($"InventarioRellenar:{empresa}:{almacen}:{hueco.Substring(0, 3)}:{hueco.Substring(3, 3)}:{hueco.Substring(6, 3)}").ConfigureAwait(false);

            InventarioPendienteNoEsta pendiente = await tx.InventarioPendiente(empresa, almacen, producto, USUARIO_INVENTARIO).ConfigureAwait(false);
            if (pendiente.DelUsuario > 0)
            {
                throw new InvalidOperationException($"hay {pendiente.DelUsuario} fila(s) de inventario de «{USUARIO_INVENTARIO}» sin contabilizar en {almacen}: avise a Informática");
            }
            if (pendiente.DelProducto > 0)
            {
                return SinRegularizar(dto, $"El {producto} se está inventariando en {almacen} (hay filas sin contabilizar): no se deja el hueco a cero. Dé la falta como siempre.");
            }

            List<ReservaPropiaNoEsta> reservas = await origen.ReservasPropias(tx, empresa, numero, producto, hueco).ConfigureAwait(false) ?? new List<ReservaPropiaNoEsta>();
            List<(ReservaPropiaNoEsta Reserva, int Cantidad)> soltadas = QueSoltar(reservas, cantidad);
            if (soltadas.Count == 0)
            {
                return SinRegularizar(dto, $"Esta salida no tiene nada reservado del {producto} en {ServicioSalidas.Hueco(hueco)}: dé la falta como siempre.");
            }
            foreach ((ReservaPropiaNoEsta reserva, int unidades) in soltadas)
            {
                await origen.SoltarAlHueco(tx, reserva, unidades, quien).ConfigureAwait(false);
            }

            // El inventario del hueco: lo libre (ya con lo soltado) a cero, contabilizado ahora
            int libre = await tx.LibreEnHueco(almacen, producto, hueco).ConfigureAwait(false);
            int stockAntes = await tx.StockAlmacen(almacen, producto).ConfigureAwait(false);
            DateTime hoy = ahora();
            int? traspaso = null;
            if (libre > 0)
            {
                await tx.InsertarFilaInventario(FilaInventario(ficha, empresa, almacen, hoy, hueco, libre)).ConfigureAwait(false);
                int retorno = await tx.Contabilizar(empresa, USUARIO_INVENTARIO, almacen).ConfigureAwait(false);
                if (retorno != 1)
                {
                    throw new InvalidOperationException($"prdContabilizarInventario ha devuelto {retorno}");
                }
                traspaso = await tx.LeerTraspasoInventario(empresa, almacen, USUARIO_INVENTARIO, hoy).ConfigureAwait(false);
                // Cuadre: ninguna fila pendiente, el hueco sin libre y el extracto justo lo que había libre menos
                InventarioPendienteNoEsta despues = await tx.InventarioPendiente(empresa, almacen, producto, USUARIO_INVENTARIO).ConfigureAwait(false);
                int libreDespues = await tx.LibreEnHueco(almacen, producto, hueco).ConfigureAwait(false);
                int stockDespues = await tx.StockAlmacen(almacen, producto).ConfigureAwait(false);
                string descuadre = Descuadre(libre, stockAntes, despues, libreDespues, stockDespues);
                if (descuadre != null)
                {
                    throw new InvalidOperationException(descuadre);
                }
            }

            dto.Regularizado = true;
            dto.UnidadesRegularizadas = libre;
            dto.TraspasoInventario = traspaso;
            dto.Cambios.Add($"{ServicioSalidas.Hueco(hueco)}: el {producto} se deja a cero en el inventario ({ServicioSalidas.Unidades(libre)} menos de stock en {almacen}).");

            // El siguiente hueco con stock calculado, en el orden del recorrido
            List<HuecoAlternativoDTO> alternativas = await tx.LibresEnOtrosHuecos(empresa, almacen, producto, hueco).ConfigureAwait(false) ?? new List<HuecoAlternativoDTO>();
            if (alternativas.Count > 1 && ordenRecorrido != null)
            {
                alternativas = ServicioCambioHuecoPicking.OrdenarAlternativas(alternativas, await ordenRecorrido.DelAlmacen(empresa, almacen).ConfigureAwait(false));
            }
            else
            {
                alternativas = ServicioCambioHuecoPicking.OrdenarAlternativas(alternativas, OrdenRecorrido.ComoHoy);
            }
            var faltan = new List<(ReservaPropiaNoEsta Reserva, int Cantidad)>();
            var reubicado = new Dictionary<string, HuecoReubicadoNoEstaDTO>();
            var libresPorHueco = new Dictionary<string, List<LibreEnHueco>>();
            foreach ((ReservaPropiaNoEsta reserva, int unidades) in soltadas)
            {
                int queda = unidades;
                foreach (HuecoAlternativoDTO destino in alternativas)
                {
                    if (queda <= 0)
                    {
                        break;
                    }
                    if (!libresPorHueco.TryGetValue(destino.Codigo, out List<LibreEnHueco> libres))
                    {
                        libres = await tx.LibresEnHueco(reserva.EmpresaLinea ?? empresa, almacen, producto, destino.Codigo).ConfigureAwait(false) ?? new List<LibreEnHueco>();
                        libresPorHueco[destino.Codigo] = libres;
                    }
                    var deEsta = new ReservaEnHueco { Ubicacion = reserva.Ubicacion, Linea = reserva.Linea, Pedido = reserva.Pedido, EmpresaLinea = reserva.EmpresaLinea, Almacen = almacen, Cantidad = queda };
                    foreach (MovimientoDeReserva movimiento in PlanificadorCambioHueco.Repartir(new[] { deEsta }, libres, queda))
                    {
                        await origen.Reservar(tx, reserva, movimiento.Libre, movimiento.Cantidad, quien).ConfigureAwait(false);
                        movimiento.Libre.Cantidad -= movimiento.Cantidad;
                        queda -= movimiento.Cantidad;
                        if (!reubicado.TryGetValue(destino.Codigo, out HuecoReubicadoNoEstaDTO h))
                        {
                            h = new HuecoReubicadoNoEstaDTO { Ubicacion = destino.Ubicacion, Codigo = destino.Codigo };
                            reubicado[destino.Codigo] = h;
                        }
                        h.Cantidad += movimiento.Cantidad;
                    }
                }
                if (queda > 0)
                {
                    faltan.Add((reserva, queda));
                }
            }
            dto.Huecos = reubicado.Values.ToList();
            dto.UnidadesReubicadas = dto.Huecos.Sum(h => h.Cantidad);
            dto.HuecoPropuesto = dto.Huecos.FirstOrDefault()?.Ubicacion;
            foreach (HuecoReubicadoNoEstaDTO h in dto.Huecos)
            {
                dto.Cambios.Add($"{ServicioSalidas.Unidades(h.Cantidad)} del {producto} se cogen de {h.Ubicacion}.");
            }

            var resultado = new ResultadoNoEsta { Estado = EstadoNoEsta.Hecho, Resultado = dto, Almacen = almacen };
            if (faltan.Count > 0)
            {
                dto.SinMasHuecos = true;
                dto.UnidadesEnFaltaDefinitiva = faltan.Sum(f => f.Cantidad);
                List<RecorteSalida> recortes = faltan.Select(f => new RecorteSalida
                {
                    Cantidad = f.Cantidad,
                    Pieza = new PiezaSalida
                    {
                        Linea = f.Reserva.Linea,
                        Pedido = f.Reserva.Pedido,
                        Producto = producto,
                        Hueco = hueco,
                        Cantidad = f.Reserva.Cantidad,
                        EsMuestra = f.Reserva.EsMuestra,
                        EsNotaEntrega = f.Reserva.EsNotaEntrega
                    }
                }).ToList();
                List<string> cambios;
                try
                {
                    cambios = await origen.FaltaDefinitiva(tx, empresa, numero, recortes, resultado.FaltasPorAvisar).ConfigureAwait(false);
                }
                catch (NoEstaNoSeRegularizaException ex)
                {
                    // Lo dice el origen antes de escribir la falta: Regularizado false y la transacción se deshace entera
                    return SinRegularizar(new ResultadoNoEstaDTO { Tipo = dto.Tipo, Numero = dto.Numero, Producto = dto.Producto, Hueco = dto.Hueco, Ensayo = dto.Ensayo },
                        ex.Message);
                }
                dto.Cambios.AddRange(cambios ?? new List<string>());
            }
            dto.Mensaje = Mensaje(dto);

            await tx.ApuntarRastro(Rastro(origen.Tipo, numero, empresa, almacen, producto, hueco, cantidad, soltadas, dto, quien, hoy)).ConfigureAwait(false);
            return resultado;
        }

        /// <summary>De las reservas de esta salida en el hueco, las unidades que se sueltan: como mucho las que no están.</summary>
        internal static List<(ReservaPropiaNoEsta Reserva, int Cantidad)> QueSoltar(IEnumerable<ReservaPropiaNoEsta> reservas, int cantidad)
        {
            var soltar = new List<(ReservaPropiaNoEsta, int)>();
            int queda = cantidad;
            foreach (ReservaPropiaNoEsta reserva in (reservas ?? Enumerable.Empty<ReservaPropiaNoEsta>()).Where(r => r != null && r.Cantidad > 0))
            {
                if (queda <= 0)
                {
                    break;
                }
                int n = Math.Min(queda, reserva.Cantidad);
                soltar.Add((reserva, n));
                queda -= n;
            }
            return soltar;
        }

        /// <summary>Null si cuadra; si no, el porqué (y se deshace todo).</summary>
        internal static string Descuadre(int libreAntes, int stockAntes, InventarioPendienteNoEsta pendienteDespues, int libreDespues, int stockDespues)
        {
            if (pendienteDespues != null && pendienteDespues.DelUsuario > 0)
            {
                return "el inventario no se ha contabilizado (queda la fila pendiente)";
            }
            if (libreDespues != 0)
            {
                return $"el hueco sigue con {libreDespues} libres después de contabilizar";
            }
            if (stockAntes - stockDespues != libreAntes)
            {
                return $"el stock del almacén ha bajado {stockAntes - stockDespues} y tenía que bajar {libreAntes}";
            }
            return null;
        }

        internal static string Mensaje(ResultadoNoEstaDTO dto)
        {
            if (dto.UnidadesReubicadas > 0 && !dto.SinMasHuecos)
            {
                return $"Hueco {dto.Hueco} puesto a cero. Vaya a {dto.HuecoPropuesto} a por {(dto.UnidadesReubicadas == 1 ? "la unidad" : $"las {dto.UnidadesReubicadas} unidades")}.";
            }
            if (dto.UnidadesReubicadas > 0)
            {
                return $"Hueco {dto.Hueco} puesto a cero. Vaya a {dto.HuecoPropuesto} a por {ServicioSalidas.Unidades(dto.UnidadesReubicadas)}; " +
                    $"{ServicioSalidas.Unidades(dto.UnidadesEnFaltaDefinitiva)} no está en ningún otro hueco y ya se ha quitado.";
            }
            return $"Hueco {dto.Hueco} puesto a cero. No queda en ningún otro hueco: es falta y ya se ha quitado de la salida.";
        }

        /// <summary>NestoAPI#858: la línea informativa de ELMAH de cada «No está» regularizado.</summary>
        internal static string LineaVigilancia(ResultadoNoEstaDTO dto, string almacen)
        {
            string huecos = dto.Huecos.Count == 0 ? "ningún otro hueco" : string.Join(", ", dto.Huecos.Select(h => $"{h.Cantidad} a {h.Ubicacion}"));
            return $"[No está] {dto.Tipo} {dto.Numero}, {dto.Producto} en {dto.Hueco} ({almacen}): hueco a cero por inventario " +
                $"({ServicioSalidas.Unidades(dto.UnidadesRegularizadas)} menos de stock, traspaso de inventario {(dto.TraspasoInventario?.ToString() ?? "ninguno")}); " +
                $"reubicadas {dto.UnidadesReubicadas} ({huecos}); falta definitiva {dto.UnidadesEnFaltaDefinitiva}. " +
                "Aviso para vigilarlo (NestoAPI#858), no es un error.";
        }

        private static Inventario FilaInventario(ProductoNoEsta ficha, string empresa, string almacen, DateTime hoy, string hueco, int libre) => new Inventario
        {
            Empresa = empresa,
            Almacén = almacen,
            Fecha = hoy.Date,
            Número = ficha.Numero?.Trim(),
            Descripción = Recortar(ficha.Descripcion, 50),
            Grupo = ficha.Grupo?.Trim(),
            Subgrupo = ficha.SubGrupo?.Trim(),
            Familia = ficha.Familia?.Trim(),
            StockCalculado = libre,
            StockReal = 0,
            Estado = ServicioRellenarInventario.ESTADO_SIN_CONTABILIZAR,
            Aplicacion = ServicioRellenarInventario.APLICACION,
            Usuario = USUARIO_INVENTARIO,
            Pasillo = hueco.Substring(0, 3),
            Fila = hueco.Substring(3, 3),
            Columna = hueco.Substring(6, 3)
        };

        private static string Recortar(string texto, int largo)
        {
            string limpio = texto?.Trim();
            return limpio != null && limpio.Length > largo ? limpio.Substring(0, largo) : limpio;
        }

        internal static Modificacion Rastro(string tipo, int numero, string empresa, string almacen, string producto, string hueco, int cantidad,
            List<(ReservaPropiaNoEsta Reserva, int Cantidad)> soltadas, ResultadoNoEstaDTO dto, string quien, DateTime fecha) => new Modificacion
        {
            Tabla = TABLA_RASTRO,
            Anterior = JsonConvert.SerializeObject(new
            {
                Empresa = empresa?.Trim(),
                Almacen = almacen,
                Tipo = tipo,
                Numero = numero,
                Producto = producto,
                Hueco = hueco,
                NoEsta = cantidad,
                Reservas = soltadas.Select(s => new { s.Reserva.Ubicacion, s.Reserva.Linea, s.Reserva.Pedido, s.Reserva.LineaEntrada, Soltadas = s.Cantidad }).ToList()
            }),
            Nuevo = JsonConvert.SerializeObject(new
            {
                Operacion = OPERACION,
                Mozo = quien,
                dto.UnidadesRegularizadas,
                dto.TraspasoInventario,
                Reubicadas = dto.Huecos.Select(h => new { h.Codigo, h.Cantidad }).ToList(),
                FaltaDefinitiva = dto.UnidadesEnFaltaDefinitiva,
                Motivo = $"«No está» en {tipo} {numero} (Carlos 12/11/26: el hueco se inventaría a cero para que no salga en otra salida)"
            }),
            Usuario = UsuarioAuditoriaHelper.ParaAuditoria(quien),
            Fecha = fecha
        };

        private static ResultadoNoEsta SinRegularizar(ResultadoNoEstaDTO dto, string motivo)
        {
            dto.Regularizado = false;
            dto.MotivoSinRegularizar = motivo;
            dto.Mensaje = motivo;
            return new ResultadoNoEsta { Estado = EstadoNoEsta.Hecho, Resultado = dto };
        }

        private static ResultadoNoEsta Error(EstadoNoEsta estado, string mensaje) => new ResultadoNoEsta { Estado = estado, Mensaje = mensaje };
    }

    /// <summary>C1, PICK: la reserva propia es la de las líneas del picking (estado 3); la falta definitiva, la del Terminar.</summary>
    public class OrigenNoEstaPicking : IOrigenNoEsta
    {
        public string Tipo => CasadorEscaneos.ORIGEN_PICKING;

        public Task<string> LeerAlmacen(ITransaccionNoEsta tx, string empresa, int numero) => tx.AlmacenPicking(empresa, numero);

        public Task<List<ReservaPropiaNoEsta>> ReservasPropias(ITransaccionNoEsta tx, string empresa, int numero, string producto, string hueco) =>
            tx.ReservasPicking(empresa, numero, producto, hueco);

        public Task SoltarAlHueco(ITransaccionNoEsta tx, ReservaPropiaNoEsta reserva, int cantidad, string usuario) =>
            tx.SoltarReservaAlHueco(reserva.Ubicacion, cantidad, usuario);

        public Task Reservar(ITransaccionNoEsta tx, ReservaPropiaNoEsta reserva, LibreEnHueco libre, int cantidad, string usuario) =>
            tx.ReservarDeLaLibre(libre.Ubicacion, cantidad, reserva.Linea, reserva.Pedido, reserva.EmpresaLinea, usuario);

        public Task<List<string>> FaltaDefinitiva(ITransaccionNoEsta tx, string empresa, int numero, List<RecorteSalida> recortes, List<FaltaPickingPedido> avisar) =>
            OrigenSalidaPicking.QuitarFaltasDeLosPedidos(empresa, numero, recortes, tx, avisar);
    }

    /// <summary>
    /// NestoAPI#858 (Carlos 12/11/26: «avanzar PDA y reposición ya, ENCENDIDO»), REPO: la salida de un traspaso de reposición aún sin
    /// terminar (salida pendiente en PreExtrProducto, sin PreparacionSalidasTerminadas). Lo propio:
    /// <list type="bullet">
    /// <item>(a) La reserva propia es la del traspaso en el hueco: Ubicaciones en estado 4 con NºTraspasoRepo (NestoAPI#651). Se
    /// suelta a libre EN SU HUECO (como la del picking, por la misma escritura) para que el inventario la quite. Las -4 de Nesto viejo
    /// no cuentan como propias (ya figuran como salida): con ellas no se regulariza.</item>
    /// <item>(b) Se reserva en el siguiente hueco como CambiarHuecoReservaReposicion (la misma escritura de la puerta de Ubicaciones):
    /// estado 4 con el traspaso y su línea de entrada; sin pasar por «pendiente de ubicar».</item>
    /// <item>(c) Falta definitiva: la unidad no estaba y el inventario ya la ha quitado del stock (ExtractoProducto, por el SP), así que
    /// sale del traspaso: menos en la salida del origen y menos en la entrada del destino (como al terminar con faltas), SIN crear
    /// «pendiente de ubicar» (no está en ninguna parte: crearlo la volvería a contar como libre). Así cuadran ExtractoProducto,
    /// PreExtrProducto, Ubicaciones y lo que la tienda espera recibir. Si con eso la salida se quedara vacía (era lo último), NO se
    /// regulariza: el Terminar no encontraría la salida («¿ya se ha sacado?») y la reposición se quedaría sin terminar; la app da la
    /// falta como siempre.</item>
    /// </list>
    /// </summary>
    public class OrigenNoEstaReposicion : IOrigenNoEsta
    {
        public string Tipo => CasadorEscaneos.ORIGEN_REPOSICION;

        public Task<string> LeerAlmacen(ITransaccionNoEsta tx, string empresa, int numero) => tx.AlmacenReposicion(empresa, numero);

        public Task<List<ReservaPropiaNoEsta>> ReservasPropias(ITransaccionNoEsta tx, string empresa, int numero, string producto, string hueco) =>
            tx.ReservasReposicion(empresa, numero, producto, hueco);

        public Task SoltarAlHueco(ITransaccionNoEsta tx, ReservaPropiaNoEsta reserva, int cantidad, string usuario) =>
            tx.SoltarReservaAlHueco(reserva.Ubicacion, cantidad, usuario);

        public Task Reservar(ITransaccionNoEsta tx, ReservaPropiaNoEsta reserva, LibreEnHueco libre, int cantidad, string usuario) =>
            tx.ReservarDeLaLibreParaElTraspaso(libre.Ubicacion, cantidad, reserva.LineaEntrada, reserva.Pedido, usuario);

        public async Task<List<string>> FaltaDefinitiva(ITransaccionNoEsta tx, string empresa, int numero, List<RecorteSalida> recortes, List<FaltaPickingPedido> avisar)
        {
            var cambios = new List<string>();
            List<IGrouping<string, RecorteSalida>> porProducto = (recortes ?? new List<RecorteSalida>()).Where(r => r.Cantidad > 0)
                .GroupBy(r => r.Pieza.Producto?.Trim()).ToList();
            int falta = porProducto.Sum(g => g.Sum(r => r.Cantidad));
            if (falta == 0)
            {
                return cambios;
            }
            int porSalir = await tx.UnidadesPorSalir(empresa, numero).ConfigureAwait(false);
            if (falta >= porSalir)
            {
                throw new NoEstaNoSeRegularizaException($"Es lo último que queda por sacar de la reposición {numero}: el hueco no se deja a cero desde aquí. " +
                    "Dé la falta como siempre.");
            }
            foreach (IGrouping<string, RecorteSalida> producto in porProducto)
            {
                int cantidad = producto.Sum(r => r.Cantidad);
                await tx.QuitarDelTraspaso(empresa, numero, producto.Key, cantidad).ConfigureAwait(false);
                cambios.Add($"{ServicioSalidas.Unidades(cantidad)} de {producto.Key} no sale en la reposición {numero}: se quita de la salida y de la entrada " +
                    "del destino (no pasa a «pendiente de ubicar»: el hueco ya está inventariado).");
            }
            return cambios;
        }
    }

    /// <summary>NestoAPI#858: el origen dice que en este caso concreto no es seguro dejar el hueco a cero (lanzado antes de escribir la falta).</summary>
    public class NoEstaNoSeRegularizaException : Exception
    {
        public NoEstaNoSeRegularizaException(string motivo) : base(motivo)
        {
        }
    }
}
