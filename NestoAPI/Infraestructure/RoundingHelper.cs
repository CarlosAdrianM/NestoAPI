using System;

namespace NestoAPI.Infraestructure
{
    public static class RoundingHelper
    {

        // Redondeo al estilo VB6 para que cuadren con Nesto viejo.
        // Una vez Nesto viejo no facture ni haga nada, se podrá cambiar por AwayFromZero.
        // Hay que cambiarlo también en el cliente WPF
        private static decimal Vb6Round(decimal value, int decimals)
        {
            double d = (double)value;
            double factor = Math.Pow(10, decimals);
            return (decimal)(Math.Round(d * factor, 0, MidpointRounding.ToEven) / factor);
        }

        public static decimal DosDecimalesRound(decimal value)
        {
            return Vb6Round(value, 2);
        }
    }
}
