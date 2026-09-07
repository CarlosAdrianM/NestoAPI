using System;

namespace NestoAPI.Models.Agencias
{
    /// <summary>
    /// NestoAPI#173: un envío que salió hace días y la agencia todavía no ha confirmado que se
    /// entregara. Sirve para llamar al cliente antes de que llame él, y como termómetro del
    /// seguimiento de cada agencia: si una deja de cerrar envíos, aquí se ve enseguida.
    /// </summary>
    public class EnvioRetrasadoDTO
    {
        public int Numero { get; set; }
        public string Empresa { get; set; }
        public int? Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public int Agencia { get; set; }
        public string NombreAgencia { get; set; }
        public string CodigoBarras { get; set; }
        public DateTime? Fecha { get; set; }

        /// <summary>
        /// Días naturales desde que salió. Lo calcula el servidor para que el listado se ordene
        /// igual lo mire quien lo mire y no dependa del reloj de cada puesto.
        /// </summary>
        public int DiasTranscurridos { get; set; }

        /// <summary>
        /// El último evento que la agencia contó de este envío ("REPARTO", "DOCUMENTADO",
        /// "RECANALIZADO"...). Es la columna que más dice de un vistazo: no es lo mismo un envío
        /// que lleva cinco días sin recogerse (DOCUMENTADO) que uno que lleva diecinueve "en
        /// reparto", que casi seguro está perdido.
        /// </summary>
        public string DetalleEstado { get; set; }

        public string Poblacion { get; set; }
        public string CodPostal { get; set; }
        public string Telefono { get; set; }
        public string Movil { get; set; }
        public string Email { get; set; }
        public string Observaciones { get; set; }
        public string Vendedor { get; set; }
    }
}
