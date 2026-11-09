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
