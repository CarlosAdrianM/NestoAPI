using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Ubicaciones
{
    // ------------------------------------------------------------------------------------------------------------------
    // NestoAPI#594: la PUERTA ÚNICA de Ubicaciones.
    //
    // Principio (Carlos, 06/10/26): «todo lo que toca ExtractoProducto en un almacén con ControlUbicaciones toca
    // Ubicaciones». Así que cada tipo de movimiento del extracto tiene aquí su operación con nombre. Corte 1: solo están
    // implementadas las de la reposición desde Algete (reservar, liberar, salida y anular la salida); las demás están
    // declaradas y lanzan NotImplementedException citando quién hace hoy ese trabajo (procedimiento o clase de la API),
    // para ir migrándolas una a una sin cambiar a los llamantes.
    //
    // Cortes siguientes: (2) tabla de movimientos (lo que hoy recibe Registrar) y consulta de cuadre contra el extracto;
    // (3) ir pasando aquí a los escritores de la API (EscriturasSalida, UbicacionesAlmacen, CierreReposiciones,
    // CambioHuecoPicking, Kits/UbicacionService, Models/Picking/GestorUbicaciones) y, según se migren sus pantallas, a los
    // procedimientos y a Nesto viejo.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Ubicaciones.Estado: lo que significa cada valor (los que se ven en los datos y en los procedimientos).</summary>
    public static class EstadosUbicacion
    {
        /// <summary>En su hueco, libre para reservar.</summary>
        public const int LIBRE = 0;
        /// <summary>En el almacén pero sin hueco (Pasillo/Fila/Columna NULL): «pendiente de ubicar».</summary>
        public const int PENDIENTE_DE_UBICAR = 2;
        /// <summary>Reservada para una línea de pedido de venta (picking, NºOrdenVta); -3 al servir el albarán.</summary>
        public const int RESERVA_PICKING = 3;
        public const int SALIDA_PICKING = -3;
        /// <summary>Reservada para una línea de reposición (NºOrdenRepo = PreExtrProducto.[Nº Orden] de la ENTRADA).</summary>
        public const int RESERVA_REPOSICION = 4;
        /// <summary>
        /// Salida de reposición: cantidad en negativo y NºTraspasoRepo. NestoAPI#651: solo cuando la salida se contabiliza
        /// (prdExtrProducto); mientras el traspaso está «por salir» la reserva sigue en 4 (viva) con su NºTraspasoRepo.
        /// </summary>
        public const int SALIDA_REPOSICION = -4;
        /// <summary>Reservada para montar un kit (prdUbicarReposicion tipo 2).</summary>
        public const int RESERVA_KIT = 5;
    }

    /// <summary>Los movimientos de Ubicaciones, uno por cada tipo de movimiento del extracto (y sus reservas).</summary>
    public enum TipoMovimientoUbicacion
    {
        // Compras
        EntradaDeCompra,
        Ubicar,
        // Pickings y albaranes
        ReservarParaPicking,
        SoltarReservaPicking,
        SalidaDePicking,
        // Reposiciones (traspasos entre almacenes)
        ReservarParaReposicion,
        LiberarReservaReposicion,
        SalidaDeReposicion,
        /// <summary>NestoAPI#651: la reserva (estado 4) se enlaza al traspaso al cerrarlo sin contabilizar (Algete); sigue viva.</summary>
        EnlazarReservaAlTraspaso,
        AnularSalidaDeReposicion,
        EntradaDeReposicion,
        /// <summary>NestoAPI#634: el ajuste del stock por una diferencia entre lo enviado y lo recibido (y su resolución).</summary>
        DiferenciaDeReposicion,
        // Kits
        MontarKit,
        DesmontarKit,
        // Inventario y huecos
        RegularizarPorInventario,
        CambiarDeHueco,
        Falta
    }

    public enum TipoDocumentoUbicacion
    {
        Ninguno,
        Picking,
        PedidoVenta,
        Albaran,
        PedidoCompra,
        AlbaranCompra,
        Traspaso,
        /// <summary>Una línea de PreExtrProducto (la entrada de una reposición mientras aún no tiene traspaso).</summary>
        LineaReposicion,
        Inventario,
        Kit
    }

    public class DocumentoUbicacion
    {
        public TipoDocumentoUbicacion Tipo { get; set; }
        public int Numero { get; set; }

        public static DocumentoUbicacion De(TipoDocumentoUbicacion tipo, int numero) => new DocumentoUbicacion { Tipo = tipo, Numero = numero };

        public override string ToString() => $"{Tipo} {Numero}";
    }

    /// <summary>Un hueco del almacén. Sin pasillo = «pendiente de ubicar».</summary>
    public class HuecoUbicacion
    {
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }

        public bool TieneHueco => !string.IsNullOrWhiteSpace(Pasillo) && !string.IsNullOrWhiteSpace(Fila) && !string.IsNullOrWhiteSpace(Columna);

        public static HuecoUbicacion De(string pasillo, string fila, string columna) =>
            new HuecoUbicacion { Pasillo = pasillo?.Trim(), Fila = fila?.Trim(), Columna = columna?.Trim() };

        /// <summary>«002/002/004», como lo pinta Ariadna; null si no tiene hueco.</summary>
        public override string ToString() => TieneHueco ? $"{Pasillo}/{Fila}/{Columna}" : null;
    }

    /// <summary>
    /// Lo que la puerta apunta de cada cambio en Ubicaciones. Corte 2 de #594: una fila de UbicacionesMovimientos por
    /// movimiento (<see cref="RegistroMovimientosUbicacionSql"/>, con el interruptor Ubicaciones:RegistrarMovimientos).
    /// </summary>
    public class MovimientoUbicacion
    {
        public TipoMovimientoUbicacion Tipo { get; set; }
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        public string Producto { get; set; }
        /// <summary>Ubicaciones.NºOrden de la fila tocada (la nueva si se inserta).</summary>
        public int? NumeroOrdenUbicacion { get; set; }
        public HuecoUbicacion HuecoOrigen { get; set; }
        public HuecoUbicacion HuecoDestino { get; set; }
        /// <summary>Unidades movidas, siempre en positivo.</summary>
        public int Cantidad { get; set; }
        public int? EstadoAnterior { get; set; }
        public int? EstadoNuevo { get; set; }
        public DocumentoUbicacion Documento { get; set; }
        public string Usuario { get; set; }
        public DateTime Momento { get; set; }

        public override string ToString() =>
            $"{Tipo} {Empresa?.Trim()}/{Almacen} {Producto} x{Cantidad} {HuecoOrigen?.ToString() ?? "-"}→{HuecoDestino?.ToString() ?? "-"} " +
            $"estado {EstadoAnterior?.ToString() ?? "-"}→{EstadoNuevo?.ToString() ?? "-"} ({Documento}) {Usuario} {Momento:dd/MM/yy HH:mm:ss}";
    }

    /// <summary>Dónde acaba lo que la puerta registra. Corte 1: Debug. Corte 2: la tabla UbicacionesMovimientos (<see cref="RegistroMovimientosUbicacionSql"/>).</summary>
    public interface IRegistroMovimientosUbicacion
    {
        void Registrar(MovimientoUbicacion movimiento);
    }

    public sealed class RegistroMovimientosUbicacionDebug : IRegistroMovimientosUbicacion
    {
        public void Registrar(MovimientoUbicacion movimiento)
        {
            Debug.WriteLine("[Ubicaciones] " + movimiento);
        }
    }

    // ---- reposición ----

    public class LineaReservaReposicion
    {
        /// <summary>PreExtrProducto.[Nº Orden] de la línea de ENTRADA (Almacén = destino): es el NºOrdenRepo de la reserva.</summary>
        public int NumeroOrdenEntrada { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
    }

    public class PiezaReservada
    {
        public int NumeroOrdenUbicacion { get; set; }
        public HuecoUbicacion Hueco { get; set; }
        public int Cantidad { get; set; }
    }

    public class ReservaLineaReposicion
    {
        public int NumeroOrdenEntrada { get; set; }
        public string Producto { get; set; }
        public int Pedida { get; set; }
        public List<PiezaReservada> Piezas { get; set; } = new List<PiezaReservada>();
        public int Reservada => Piezas.Sum(p => p.Cantidad);
        /// <summary>Unidades que el mozo no encontrará en un hueco: las no reservadas y las reservadas de «pendiente de ubicar».</summary>
        public int UnidadesSinHueco => Pedida - Piezas.Where(p => p.Hueco?.TieneHueco == true).Sum(p => p.Cantidad);
    }

    /// <summary>Lo que ha hecho una operación de la puerta: filas de Ubicaciones tocadas y los movimientos registrados.</summary>
    public class ResumenUbicaciones
    {
        public int FilasTocadas { get; set; }
        public List<MovimientoUbicacion> Movimientos { get; set; } = new List<MovimientoUbicacion>();
        public List<ReservaLineaReposicion> Reservas { get; set; } = new List<ReservaLineaReposicion>();
        public IEnumerable<string> Huecos => Movimientos.Select(m => (m.HuecoDestino ?? m.HuecoOrigen)?.ToString()).Where(h => h != null).Distinct();
    }

    /// <summary>
    /// NestoAPI#594: la ÚNICA clase que escribe en Ubicaciones (para lo nuevo). No abre transacciones: la llama un
    /// servicio dentro de la suya, con el mismo contexto, para que los huecos y el extracto vayan juntos o no vayan.
    /// Cada operación devuelve qué ha tocado y pasa cada cambio por un único punto de registro.
    /// </summary>
    public interface IPuertaUbicaciones
    {
        // ---------------------------------------------------------------- Compras
        /// <summary>Entrada de un albarán de compra: «pendiente de ubicar» (estado 2). Hoy: prdCrearAlbaránCmp / prdExtrProducto.</summary>
        Task<ResumenUbicaciones> EntradaDeCompra(string empresa, string almacen, string producto, int cantidad, DocumentoUbicacion documento, string usuario);

        /// <summary>Dar hueco a lo pendiente de ubicar (2 → 0). Hoy: prdUbicar (UbicacionesAlmacen, GestorUbicaciones).</summary>
        Task<ResumenUbicaciones> Ubicar(string empresa, string almacen, string producto, int cantidad, HuecoUbicacion destino, string usuario);

        // ---------------------------------------------------------------- Pickings y albaranes
        /// <summary>Reservar de lo libre para una línea de pedido (0/2 → 3). Hoy: prdUbicacionPicking.</summary>
        Task<ResumenUbicaciones> ReservarParaPicking(string empresa, string almacen, int numeroOrdenLineaVenta, string producto, int cantidad, string usuario);

        /// <summary>Soltar la reserva de una línea (3 → 0/2). Hoy: prdDeshacerUbicacionPicking, prdCambiarCantidadPicking, EscriturasSalida.</summary>
        Task<ResumenUbicaciones> SoltarReservaPicking(string empresa, int numeroOrdenLineaVenta, int cantidad, string usuario);

        /// <summary>Salida al servir el albarán (3 → -3). Hoy: prdCrearAlbaránVta, prdAgruparAlbaranesVta, prdAgruparOfertasPedido.</summary>
        Task<ResumenUbicaciones> SalidaDePicking(string empresa, int numeroOrdenLineaVenta, DocumentoUbicacion albaran, string usuario);

        // ---------------------------------------------------------------- Reposiciones
        /// <summary>
        /// Reservar en el almacén de origen para cada línea de ENTRADA de una reposición (0/2 → 4, NºOrdenRepo = la línea).
        /// Mismo resultado que prdUbicarReposicion tipo 1: un hueco si cabe entero (FIFO/LIFO según Almacenes.FIFO), si no,
        /// de varios; si no hay nada libre, la línea se queda sin reserva («sin hueco»).
        /// </summary>
        Task<ResumenUbicaciones> ReservarParaReposicion(string empresa, string espejo, string almacenOrigen,
            IReadOnlyList<LineaReservaReposicion> lineas, string usuario);

        /// <summary>Devolver al hueco la reserva de una línea (4 → 0/2). Mismo resultado que prdDeshacerUbicacionReposicion.</summary>
        Task<ResumenUbicaciones> LiberarReservaReposicion(string empresa, int numeroOrdenEntrada, string usuario);

        /// <summary>
        /// Al cerrar el traspaso Y contabilizar su salida a la vez (las tiendas, Terminar): las reservas de esas líneas pasan a
        /// salida (4 → -4, cantidad negativa, NºTraspasoRepo).
        /// </summary>
        Task<ResumenUbicaciones> SalidaDeReposicion(string empresa, IReadOnlyList<int> numerosOrdenEntrada, int numeroTraspaso, string usuario);

        /// <summary>
        /// NestoAPI#651: al cerrar el traspaso SIN contabilizar la salida (Algete: la contabiliza Ariadna al terminar la
        /// recogida). Las reservas de esas líneas se quedan en estado 4, positivas (siguen en el almacén: el extracto aún las
        /// cuenta), y se enlazan al traspaso (NºTraspasoRepo) para que el recorrido, las faltas y anular las encuentren.
        /// </summary>
        Task<ResumenUbicaciones> EnlazarReservasAlTraspaso(string empresa, IReadOnlyList<int> numerosOrdenEntrada, int numeroTraspaso, string usuario);

        /// <summary>
        /// NestoAPI#651: al contabilizar la salida de un traspaso ya cerrado (Ariadna, terminar la recogida), en la misma
        /// transacción que prdExtrProducto: sus reservas en el origen (estado 4 con NºTraspasoRepo) pasan a salida (4 → -4,
        /// cantidad negativa). Las de un traspaso cerrado antes de #651 ya están en -4 y no se tocan.
        /// </summary>
        Task<ResumenUbicaciones> SalidaDeReposicionDelTraspaso(string empresa, string almacenOrigen, int numeroTraspaso, string usuario);

        /// <summary>
        /// Anular un traspaso aún sin recoger ni contabilizar: lo reservado vuelve a su hueco (4 → 0/2; y -4 → 0/2 en los
        /// cerrados antes de NestoAPI#651).
        /// </summary>
        Task<ResumenUbicaciones> AnularSalidaDeReposicion(string empresa, string almacenOrigen, int numeroTraspaso, string usuario);

        /// <summary>La entrada en el destino, «pendiente de ubicar» con NºTraspasoRepo. Hoy: CierreReposiciones / prdExtrProducto.</summary>
        Task<ResumenUbicaciones> EntradaDeReposicion(string empresa, string almacenDestino, string producto, int cantidad, int numeroTraspaso, string usuario);

        /// <summary>
        /// NestoAPI#634: lo que entra en un almacén con control de ubicaciones por una diferencia de reposición (el ajuste del
        /// origen al recibir de menos, o una resolución): «pendiente de ubicar» (estado 2), como las demás entradas, para que al
        /// comprobarla se ubique. Sin NºTraspasoRepo: no es la entrada del traspaso en su destino (lo mismo que haría
        /// prdExtrProducto con un diario que no es el de entrada de reposiciones). El llamante ya sabe que el almacén lleva
        /// control de ubicaciones y el producto se ubica.
        /// </summary>
        Task<ResumenUbicaciones> EntradaPorDiferenciaReposicion(string empresa, string almacen, string producto, int cantidad, int numeroTraspaso, string usuario);

        /// <summary>
        /// NestoAPI#634: lo que sale de un almacén con control de ubicaciones por una diferencia de reposición (el ajuste del
        /// origen al recibir de más, o una resolución). Se quita de lo libre y sin reservar, en este orden (ver
        /// <see cref="PlanificadorDiferenciaUbicaciones"/>): el hueco del que salió el traspaso y lo «pendiente de ubicar» (o al
        /// revés con <paramref name="primeroPendienteDeUbicar"/>) y, si aún falta, el único hueco con stock del producto (como
        /// prdExtrProducto con una salida sin hueco). Lo que no se encuentre se queda: la diferencia es
        /// <paramref name="cantidad"/> menos lo movido, y la dice el llamante.
        /// </summary>
        Task<ResumenUbicaciones> SalidaPorDiferenciaReposicion(string empresa, string almacen, string producto, int cantidad, int numeroTraspaso,
            bool primeroPendienteDeUbicar, string usuario);

        // ---------------------------------------------------------------- Kits
        /// <summary>Montar un kit: reservar los componentes (estado 5) y dar entrada al kit. Hoy: prdUbicarReposicion tipo 2, Kits/UbicacionService.</summary>
        Task<ResumenUbicaciones> MontarKit(string empresa, string almacen, string kit, int cantidad, string usuario);

        /// <summary>Desmontar un kit: los componentes vuelven a pendiente de ubicar. Hoy: Kits/UbicacionService.</summary>
        Task<ResumenUbicaciones> DesmontarKit(string empresa, string almacen, string kit, int cantidad, string usuario);

        // ---------------------------------------------------------------- Inventario, huecos y faltas
        /// <summary>Regularizar por inventario. Hoy: prdContabilizarInventario, prdDeshacerInventario.</summary>
        Task<ResumenUbicaciones> RegularizarPorInventario(string empresa, string almacen, int numeroTraspasoInventario, string usuario);

        /// <summary>Cambiar de hueco (también las reservas). Hoy: CambioHuecoPicking (Ariadna#12), GestorUbicaciones.</summary>
        Task<ResumenUbicaciones> CambiarDeHueco(string empresa, string almacen, string producto, HuecoUbicacion origen, HuecoUbicacion destino, int cantidad, string usuario);

        /// <summary>Lo que no está en su hueco al recoger: pasa a «pendiente de ubicar». Hoy: EscriturasSalida (faltas de Ariadna).</summary>
        Task<ResumenUbicaciones> Falta(string empresa, int numeroOrdenUbicacion, int cantidad, string usuario);
    }

    /// <summary>Una fila libre de Ubicaciones candidata a una salida por diferencia: si está en el hueco del que salió el traspaso.</summary>
    public class FilaUbicacionDiferencia : FilaUbicacionLibre
    {
        public bool DelTraspaso { get; set; }
    }

    /// <summary>
    /// NestoAPI#634: de qué filas libres (estado 0 con hueco o 2 «pendiente de ubicar», sin reservar) se quita una salida por
    /// diferencia de reposición. Puro: sin base de datos.
    /// <list type="number">
    /// <item>Al recibir de más (lo que salió de más estaba donde se cogió lo demás): primero el hueco del que salió el
    /// traspaso; después lo pendiente de ubicar.</item>
    /// <item>Al resolver (<c>primeroPendienteDeUbicar</c>: se deshace lo que puso el ajuste al recibir): primero lo pendiente
    /// de ubicar, lo más nuevo antes; después el hueco del traspaso.</item>
    /// <item>Si aún falta, el resto de huecos solo si el producto está en UNO (como prdExtrProducto con una salida sin hueco:
    /// con varios no se sabe de cuál; se queda sin quitar y se avisa).</item>
    /// </list>
    /// </summary>
    public static class PlanificadorDiferenciaUbicaciones
    {
        public static List<TomaDeHueco> Repartir(IEnumerable<FilaUbicacionDiferencia> filas, int cantidad, bool primeroPendienteDeUbicar)
        {
            var tomas = new List<TomaDeHueco>();
            if (cantidad <= 0)
            {
                return tomas;
            }
            List<FilaUbicacionDiferencia> validas = (filas ?? Enumerable.Empty<FilaUbicacionDiferencia>())
                .Where(f => f != null && f.Cantidad > 0 && (f.Estado == EstadosUbicacion.LIBRE || f.Estado == EstadosUbicacion.PENDIENTE_DE_UBICAR))
                .ToList();
            List<FilaUbicacionDiferencia> pendientes = validas.Where(f => !EnHueco(f))
                .OrderByDescending(f => f.FechaCreacion).ThenByDescending(f => f.NumeroOrden).ToList();
            List<FilaUbicacionDiferencia> delTraspaso = validas.Where(f => EnHueco(f) && f.DelTraspaso)
                .OrderByDescending(f => f.NumeroOrden).ToList();
            List<FilaUbicacionDiferencia> resto = validas.Where(f => EnHueco(f) && !f.DelTraspaso).ToList();
            List<FilaUbicacionDiferencia> unico = resto.Select(f => f.Hueco.ToString()).Distinct().Count() == 1
                ? resto.OrderByDescending(f => f.NumeroOrden).ToList()
                : new List<FilaUbicacionDiferencia>();

            IEnumerable<FilaUbicacionDiferencia> orden = (primeroPendienteDeUbicar ? pendientes.Concat(delTraspaso) : delTraspaso.Concat(pendientes))
                .Concat(unico);
            int queda = cantidad;
            foreach (FilaUbicacionDiferencia fila in orden)
            {
                if (queda == 0)
                {
                    break;
                }
                int coger = Math.Min(queda, fila.Cantidad);
                tomas.Add(new TomaDeHueco { Libre = fila, Cantidad = coger });
                queda -= coger;
            }
            return tomas;
        }

        private static bool EnHueco(FilaUbicacionDiferencia fila)
        {
            return fila.Estado == EstadosUbicacion.LIBRE && fila.Hueco.TieneHueco;
        }
    }
}
