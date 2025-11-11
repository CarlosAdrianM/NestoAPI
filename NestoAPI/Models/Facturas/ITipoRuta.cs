using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Interfaz para definir comportamientos específicos de tipos de rutas.
    /// Permite extensibilidad: agregar un nuevo tipo de ruta solo requiere
    /// crear una nueva implementación de esta interfaz.
    /// </summary>
    public interface ITipoRuta
    {
        /// <summary>
        /// Identificador único del tipo de ruta.
        /// Se usa para seleccionar el tipo de ruta de forma programática.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Nombre descriptivo del tipo de ruta para mostrar en UI (radio buttons, etc.)
        /// Ejemplo: "Ruta Propia", "Ruta de Agencias"
        /// </summary>
        string NombreParaMostrar { get; }

        /// <summary>
        /// Descripción detallada del comportamiento de este tipo de ruta.
        /// Útil para tooltips o ayuda en la UI.
        /// </summary>
        string Descripcion { get; }

        /// <summary>
        /// Lista de números de ruta que pertenecen a este tipo.
        /// Ejemplo: RutaPropia podría contener ["AT", "16"]
        /// </summary>
        IReadOnlyList<string> RutasContenidas { get; }

        /// <summary>
        /// Verifica si este tipo de ruta contiene un número de ruta específico.
        /// </summary>
        /// <param name="numeroRuta">Número de ruta a verificar</param>
        /// <returns>True si esta implementación maneja esa ruta</returns>
        bool ContieneRuta(string numeroRuta);

        /// <summary>
        /// Determina el número de copias a imprimir para un documento (factura, albarán o nota de entrega).
        /// </summary>
        /// <param name="pedido">Pedido asociado al documento</param>
        /// <param name="debeImprimirDocumento">Indica si el documento tiene comentario de impresión física</param>
        /// <param name="empresaPorDefecto">Empresa por defecto del sistema (para comparar si fue traspasado)</param>
        /// <returns>Número de copias a imprimir (0 = no imprimir, 1 = solo original, 2 = original + 1 copia, etc.)</returns>
        int ObtenerNumeroCopias(CabPedidoVta pedido, bool debeImprimirDocumento, string empresaPorDefecto);

        /// <summary>
        /// Obtiene la bandeja de impresión a utilizar.
        /// </summary>
        /// <returns>Nombre de la bandeja ("Default", "Tray1", "Tray2", etc.)</returns>
        string ObtenerBandeja();

        /// <summary>
        /// Indica si este tipo de ruta requiere insertar registros en ExtractoRuta.
        /// Ruta Propia: true (requiere registro contable)
        /// Ruta de Agencias: false (no requiere registro)
        /// </summary>
        /// <returns>True si debe insertar en ExtractoRuta, false en caso contrario</returns>
        bool DebeInsertarEnExtractoRuta();
    }
}
