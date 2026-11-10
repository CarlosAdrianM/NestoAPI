using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>NestoAPI#590: el cuerpo de un error de api/Almacen como objeto, para que cualquier cliente muestre el motivo.</summary>
    public class MensajeAlmacenDTO
    {
        public string Message { get; set; }
    }

    /// <summary>NestoAPI#590: POST api/Picking/{empresa}/{pedido}/QuitarPicking.</summary>
    public class QuitarPickingDTO
    {
        /// <summary>
        /// El usuario ya ha visto lo que el mozo ha leído de este pedido y confirma: se quita el picking y esas lecturas se anulan
        /// (el mozo las aparta para el pedido). Sin él, si hay lecturas, 409 con ellas y no se toca nada.
        /// </summary>
        public bool Confirmado { get; set; }

        /// <summary>
        /// Novedad 1146 (Alfredo, 10/11/26: «lo más común es quitar alguna línea, por ejemplo, los portes»): los Nº Orden de las
        /// líneas de LinPedidoVta a las que quitar el picking. Sin ellas (o vacía), todas las pendientes del pedido, como siempre.
        /// </summary>
        public List<int> Lineas { get; set; }
    }

    /// <summary>Novedad 1146: una línea del pedido a la que se ha quitado el picking.</summary>
    public class LineaQuitadaPickingDTO
    {
        public int Linea { get; set; }
        public int Picking { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Es un producto que el mozo recoge en la PDA (TipoLinea 1, no ficticio). Los portes, textos o cuentas, no.</summary>
        public bool SeRecoge { get; set; }
    }

    /// <summary>Novedad 1146: GET api/Picking/{empresa}/{pedido}/LineasConPicking (para elegir a qué líneas quitar el picking).</summary>
    public class LineaConPickingDTO
    {
        /// <summary>Nº Orden de LinPedidoVta.</summary>
        public int Linea { get; set; }
        public int Picking { get; set; }
        public short Estado { get; set; }
        public short TipoLinea { get; set; }
        public string Producto { get; set; }
        public string Texto { get; set; }
        /// <summary>Lo que se recoge: Cantidad - Recoger.</summary>
        public int Cantidad { get; set; }
        public bool SePuedeQuitar { get; set; }
        /// <summary>Por qué no se puede quitar (vacío si se puede).</summary>
        public string Motivo { get; set; }
    }

    /// <summary>NestoAPI#590: lo que un mozo ya ha leído en el picking de los productos del pedido que se quita (lo que hay que apartar).</summary>
    public class LecturaQuitarPickingDTO
    {
        public int Picking { get; set; }
        public string Usuario { get; set; }
        public string Dispositivo { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>
    /// NestoAPI#590: la respuesta de QuitarPicking. 200 (quitado), 409 con <see cref="Lecturas"/> (hay que confirmar) o 409 sin
    /// ellas (no se puede: albarán, packing, terminado).
    /// </summary>
    public class ResultadoQuitarPickingDTO
    {
        public string Message { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>Los pickings de los que ha salido (o saldría) el pedido.</summary>
        public List<int> Pickings { get; set; } = new List<int>();
        /// <summary>Las líneas a las que se ha quitado el picking.</summary>
        public int Lineas { get; set; }
        /// <summary>Lo leído de este pedido en el picking: en el 409, lo que habría que apartar; en el 200, lo que se ha anulado.</summary>
        public List<LecturaQuitarPickingDTO> Lecturas { get; set; } = new List<LecturaQuitarPickingDTO>();
        /// <summary>Novedad 1146: se han pedido líneas concretas (no todo el pedido).</summary>
        public bool Parcial { get; set; }
        /// <summary>Novedad 1146: las líneas a las que se ha quitado el picking (en el 200).</summary>
        public List<LineaQuitadaPickingDTO> LineasQuitadas { get; set; } = new List<LineaQuitadaPickingDTO>();
    }

    /// <summary>
    /// NestoAPI#590: el aviso por SignalR («pedidoSacadoDelPicking») a las PDA que están en el picking: el pedido que ha salido, qué
    /// hay que apartar (lo que ya se había leído) y quién lo ha hecho. Detrás va siempre el «haCambiado» de siempre.
    /// </summary>
    public class PedidoSacadoDelPickingAvisoDTO
    {
        public string TipoOrigen { get; set; } = "PICK";
        public int NumeroOrigen { get; set; }
        public int Pedido { get; set; }
        public string Usuario { get; set; }
        public string Mensaje { get; set; }
        public List<LecturaQuitarPickingDTO> Apartar { get; set; } = new List<LecturaQuitarPickingDTO>();
    }
}
