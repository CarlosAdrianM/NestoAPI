using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>
    /// Ariadna#43: una lectura que no ha casado (un código que no es de ningún producto de la lista, o que es de otro producto
    /// que el que toca). Ariadna la apunta sin molestar al mozo (POST api/Almacen/Lecturas/NoCasadas).
    /// </summary>
    public class LecturaNoCasadaDTO
    {
        /// <summary>Lo leído (o tecleado) tal cual, sin espacios alrededor.</summary>
        public string Codigo { get; set; }
        /// <summary>El producto que tocaba (la línea actual); null si no había ninguno.</summary>
        public string ProductoEsperado { get; set; }
        /// <summary>Si el código es de otro producto de la lista, cuál; null si no es de ninguno.</summary>
        public string ProductoLeido { get; set; }
        /// <summary>Recorrido, Packing, EnviarReposicion, Recibir, Ubicar…</summary>
        public string Pantalla { get; set; }
        /// <summary>PICK, REPO, COMP… (lo que se estaba preparando o recibiendo); null si no aplica.</summary>
        public string TipoOrigen { get; set; }
        public int? NumeroOrigen { get; set; }
        /// <summary>True: con el lector o la cámara; false: tecleado; null: no se sabe.</summary>
        public bool? ConEscaner { get; set; }
        public string Dispositivo { get; set; }
        /// <summary>La hora del aparato.</summary>
        public DateTime FechaLectura { get; set; }
    }

    public class ResultadoLecturasNoCasadasDTO
    {
        public int Guardadas { get; set; }
        /// <summary>Las que no se han guardado (sin código, o un código de más de 50 caracteres).</summary>
        public int Descartadas { get; set; }
    }

    /// <summary>
    /// Ariadna#43: por qué se ha tecleado un producto que tiene un código de barras válido. Se pregunta una sola vez por producto.
    /// </summary>
    public static class MotivosTecleo
    {
        /// <summary>El código del envase no se lee (ni con el lector ni con la cámara): «código que se lee mal».</summary>
        public const string NO_SE_LEE = "NOSELEE";
        /// <summary>El envase no lleva el código (el de la ficha es de otro envase, o no lo trae impreso).</summary>
        public const string SIN_CODIGO = "SINCODIGO";
        public const string OTRO = "OTRO";

        public static readonly IReadOnlyList<string> TODOS = new[] { NO_SE_LEE, SIN_CODIGO, OTRO };
    }

    /// <summary>Ariadna#43: POST api/Almacen/Lecturas/MotivoTecleo.</summary>
    public class MotivoTecleoDTO
    {
        public string Producto { get; set; }
        /// <summary>NOSELEE, SINCODIGO u OTRO (<see cref="MotivosTecleo"/>).</summary>
        public string Motivo { get; set; }
        /// <summary>El código válido que tenía el producto cuando se preguntó (para que Compras sepa cuál no se lee).</summary>
        public string Codigo { get; set; }
        public string Dispositivo { get; set; }
    }

    /// <summary>Ariadna#43: si ya se ha preguntado por qué se teclea un producto, y qué se contestó.</summary>
    public class EstadoMotivoTecleoDTO
    {
        public string Producto { get; set; }
        /// <summary>Ya se ha contestado alguna vez: no se vuelve a preguntar.</summary>
        public bool Preguntado { get; set; }
        public string Motivo { get; set; }
        public string Codigo { get; set; }
        public DateTime? Fecha { get; set; }
        public string Mensaje { get; set; }
    }
}
