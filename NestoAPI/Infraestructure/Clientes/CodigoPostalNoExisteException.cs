using System;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#821 (Carlos 05/11/26): el CP español no está en CódigosPostales y no se puede dar de alta
    /// (la dirección no viene del buscador de Google, o Google no trae población/provincia). Sigue siendo
    /// un <see cref="ArgumentException"/> (como el «No existe el código postal» de antes) para los
    /// llamantes que ya lo capturan; el tipo propio deja al PUT/POST de clientes devolver un 400 con el
    /// motivo sin convertir en 400 cualquier otro ArgumentException (que es un fallo nuestro).
    /// </summary>
    public class CodigoPostalNoExisteException : ArgumentException
    {
        public CodigoPostalNoExisteException(string mensaje) : base(mensaje)
        {
        }
    }
}
