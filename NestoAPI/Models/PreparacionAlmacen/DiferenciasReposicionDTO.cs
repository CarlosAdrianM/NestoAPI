using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#634: una diferencia entre lo enviado y lo recibido en una reposición (tabla DiferenciasReposiciones), para la
    /// ventana «Diferencias de reposición» de Nesto (Nesto#526) y la comprobación desde la PDA (Ariadna#25).
    /// </summary>
    public class DiferenciaReposicionDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int Traspaso { get; set; }
        /// <summary>Donde se comprueba (de donde salió). Null si no se sabe.</summary>
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public int Enviado { get; set; }
        public int Leido { get; set; }
        /// <summary>Leído menos enviado: negativo falta (se recibió de menos), positivo sobra (se recibió de más).</summary>
        public int Diferencia { get; set; }
        /// <summary>«Falta» o «Sobra».</summary>
        public string Tipo => Diferencia < 0 ? "Falta" : "Sobra";
        /// <summary>«La tienda recibió 0 de 1», «Recibió 7 de 6».</summary>
        public string QuePaso { get; set; }
        /// <summary>Qué hay que comprobar en el origen, en palabras del almacén.</summary>
        public string QueComprobar { get; set; }
        /// <summary>El hueco del que salió el producto con este traspaso («005/005/004»), si lo hay.</summary>
        public string HuecoSalida { get; set; }
        /// <summary>Los huecos donde hay ahora stock libre del producto en el origen, separados por comas.</summary>
        public string Huecos { get; set; }
        public DateTime FechaRecepcion { get; set; }
        public string UsuarioRecepcion { get; set; }
        /// <summary>Quien creó o preparó el traspaso.</summary>
        public string Preparador { get; set; }

        /// <summary>Null (por hacer), «Hecho», «Apagado» (el interruptor estaba apagado: no se tocó el stock) o «Fallido».</summary>
        public string Ajuste { get; set; }
        public string AjusteMensaje { get; set; }
        /// <summary>ExtractoProducto.[Nº Orden] del ajuste en el origen.</summary>
        public int? NumOrdenAjuste { get; set; }

        /// <summary>«Pendiente» o «Resuelta».</summary>
        public string Estado { get; set; }
        /// <summary>Null, «Confirmada», «NoEstaba», «Sobraba» o «ErrorRecepcion».</summary>
        public string Resolucion { get; set; }
        public string UsuarioResolucion { get; set; }
        public DateTime? FechaResolucion { get; set; }
        public string Comentario { get; set; }
        public int? NumOrdenResolucion { get; set; }
        public int? NumOrdenResolucionDestino { get; set; }

        /// <summary>Las resoluciones que se pueden elegir (vacía si ya está resuelta).</summary>
        public List<string> ResolucionesPosibles { get; set; } = new List<string>();
        /// <summary>El origen lleva control de ubicaciones (Algete): solo la resuelven Almacén y Dirección.</summary>
        public bool OrigenConControlUbicaciones { get; set; }
    }

    /// <summary>NestoAPI#634: POST api/Reposiciones/Diferencias/{id}/Resolver.</summary>
    public class ResolverDiferenciaReposicionDTO
    {
        /// <summary>«Confirmada», «NoEstaba», «Sobraba» o «ErrorRecepcion».</summary>
        public string Resolucion { get; set; }
        public string Comentario { get; set; }
    }

    public class ResultadoResolverDiferenciaReposicionDTO
    {
        public DiferenciaReposicionDTO Diferencia { get; set; }
        /// <summary>Lo que ha pasado con el stock, para enseñárselo a quien resuelve.</summary>
        public string Mensaje { get; set; }
    }

    public class ContadorDiferenciasReposicionDTO
    {
        public string Almacen { get; set; }
        public int Pendientes { get; set; }
    }
}
