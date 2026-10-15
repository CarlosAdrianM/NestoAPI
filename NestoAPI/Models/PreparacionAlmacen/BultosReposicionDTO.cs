using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    // NestoAPI#645 (corte 1): una etiqueta con código de barras por bulto al cerrar una reposición y lectura obligatoria al
    // recibirla (Nesto#530, Ariadna#31).

    /// <summary>POST api/Reposiciones/{traspaso}/Bultos: cuántos bultos lleva (de 1 a 50) y si se imprimen sus etiquetas.</summary>
    public class CrearBultosReposicionDTO
    {
        public int Bultos { get; set; }
        public bool Imprimir { get; set; } = true;
    }

    /// <summary>Un bulto de una reposición, como se enseña y se imprime.</summary>
    public class BultoReposicionDTO
    {
        /// <summary>Lo que lleva el código de barras: «R81010-2».</summary>
        public string Codigo { get; set; }
        public int Bulto { get; set; }
        public int Total { get; set; }
        /// <summary>Lo que va debajo del código: «81010 · bulto 2/3 · ALC→ALG».</summary>
        public string Texto { get; set; }
    }

    public class ResultadoBultosReposicionDTO
    {
        public int Traspaso { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>Donde se han mandado las etiquetas. Null si no se ha pedido imprimir.</summary>
        public string Impresora { get; set; }
        public List<BultoReposicionDTO> Bultos { get; set; } = new List<BultoReposicionDTO>();
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>GET api/Reposiciones/Bultos/{codigo}: de qué reposición es una etiqueta y si ya se ha leído al recibir.</summary>
    public class LecturaBultoReposicionDTO
    {
        public string Codigo { get; set; }
        public int Traspaso { get; set; }
        public int Bulto { get; set; }
        public int Total { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public bool Leido { get; set; }
        public DateTime? FechaLectura { get; set; }
    }
}
