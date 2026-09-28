using System;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Aritmética del tipo <c>money</c> de SQL Server, tal como la usa el SP de precios medios (Issue #547).
    ///
    /// <c>money</c> guarda 4 decimales y, al DIVIDIR, TRUNCA hacia cero (no redondea). Verificado en el
    /// servidor: 385,995 / 12 = 32,1662 (no 32,1663); 2 / 3 = 0,6666; -1,091 / 6 = -0,1818.
    /// <c>int × money</c> y <c>money + money</c> son exactos, así que no necesitan nada.
    ///
    /// ÚNICO punto del cálculo de precios medios donde se "redondea": si mañana se decide redondear en vez
    /// de truncar, se cambia aquí (con su test en rojo primero). No usar RoundingHelper: esto no es redondeo
    /// comercial, es la réplica exacta de lo que hace el SP para poder comparar al diezmilésimo.
    /// </summary>
    public static class AritmeticaMoney
    {
        private const decimal ESCALA = 10000m;

        /// <summary>
        /// <c>money / int</c> de SQL Server: división truncada a 4 decimales hacia cero.
        /// </summary>
        public static decimal Dividir(decimal numerador, int denominador)
        {
            if (denominador == 0)
            {
                // El SP protege todas sus divisiones con el WHERE; si llegamos aquí es un fallo de la calculadora.
                throw new DivideByZeroException("División money entre 0 en el cálculo del precio medio");
            }
            return Truncar(numerador / denominador);
        }

        /// <summary>
        /// Trunca a 4 decimales hacia cero (lo que hace SQL Server al guardar el resultado de una división money).
        /// </summary>
        public static decimal Truncar(decimal valor)
        {
            return Math.Truncate(valor * ESCALA) / ESCALA;
        }
    }
}
