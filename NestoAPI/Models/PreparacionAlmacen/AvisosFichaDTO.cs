using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>
    /// Ariadna#8: el mozo informa de un dato mal en la ficha de un producto. Campos: Foto, Precio, Nombre, Familia,
    /// Subgrupo, Tamano (tamaño y unidad), CodigoBarras u Otro; se pueden marcar varios.
    /// </summary>
    public class InformarDatoMalDTO
    {
        public string Producto { get; set; }
        public List<string> Campos { get; set; } = new List<string>();
        /// <summary>Cómo debería ser, en palabras del mozo.</summary>
        public string Comentario { get; set; }
        /// <summary>El código que ha leído el mozo si no casa con el producto (para darlo de alta o corregirlo).</summary>
        public string CodigoLeido { get; set; }
        /// <summary>
        /// NestoAPI#604: true si CodigoLeido entró por el escáner (de golpe), false si se tecleó; null si la app no lo
        /// dice (versiones antiguas de Ariadna).
        /// </summary>
        public bool? CodigoLeidoConEscaner { get; set; }
        /// <summary>
        /// NestoAPI#604: el mozo ya ha visto el aviso de «comprueba» (el código es de otro producto o el de la ficha) y
        /// quiere avisar igual. Sin él, en esos casos no se crea el aviso: se le devuelve Comprobar = true.
        /// </summary>
        public bool Confirmado { get; set; }
        /// <summary>La foto que ha visto el mozo (si no viene, la que tenga la tienda ahora).</summary>
        public string UrlFoto { get; set; }
        public string Dispositivo { get; set; }
    }

    public class ResultadoInformarDatoMalDTO
    {
        public string Mensaje { get; set; }
        /// <summary>
        /// NestoAPI#604: no se ha avisado a nadie. El Mensaje le dice al mozo qué comprobar; si quiere avisar igual,
        /// vuelve a enviar con Confirmado = true.
        /// </summary>
        public bool Comprobar { get; set; }
        /// <summary>Los avisos creados o a los que se ha sumado (uno por equipo: Tienda online y/o Compras).</summary>
        public List<int> Avisos { get; set; } = new List<int>();
    }

    /// <summary>Ariadna#8: quien recibe el aviso lo cierra con «Cambiado» o «EstabaBien».</summary>
    public class CerrarAvisoFichaDTO
    {
        public string Resultado { get; set; }
    }
}
