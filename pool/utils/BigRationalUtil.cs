

using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text;

#pragma warning disable 3021 
namespace XPool.utils
{
    
        

    [Serializable]
    [ComVisible(false)]
    public struct BigRational : IComparable, IComparable<BigRational>, IDeserializationCallback,
        IEquatable<BigRational>,
        ISerializable
    {
                private BigInteger m_numerator;

        
        #region Members for Internal Support

        [StructLayout(LayoutKind.Explicit)]
        internal struct DoubleUlong
        {
            [FieldOffset(0)] public double dbl;

            [FieldOffset(0)] public ulong uu;
        }

        private const int DoubleMaxScale = 308;
        private static readonly BigInteger s_bnDoublePrecision = BigInteger.Pow(10, DoubleMaxScale);
        private static readonly BigInteger s_bnDoubleMaxValue = (BigInteger) double.MaxValue;
        private static readonly BigInteger s_bnDoubleMinValue = (BigInteger) double.MinValue;

        [StructLayout(LayoutKind.Explicit)]
        internal struct DecimalUInt32
        {
            [FieldOffset(0)] public decimal dec;

            [FieldOffset(0)] public int flags;
        }

        private const int DecimalScaleMask = 0x00FF0000;
        private const int DecimalSignMask = unchecked((int) 0x80000000);
        private const int DecimalMaxScale = 28;
        private static readonly BigInteger s_bnDecimalPrecision = BigInteger.Pow(10, DecimalMaxScale);
        private static readonly BigInteger s_bnDecimalMaxValue = (BigInteger) decimal.MaxValue;
        private static readonly BigInteger s_bnDecimalMinValue = (BigInteger) decimal.MinValue;

        private const string c_solidus = @"/";

        #endregion Members for Internal Support

        
        #region Public Properties

        public static BigRational Zero { get; } = new BigRational(BigInteger.Zero);

        public static BigRational One { get; } = new BigRational(BigInteger.One);

        public static BigRational MinusOne { get; } = new BigRational(BigInteger.MinusOne);

        public int Sign => m_numerator.Sign;

        public BigInteger Numerator => m_numerator;

        public BigInteger Denominator { get; private set; }

        #endregion Public Properties

        
        #region Public Instance Methods

                                                                                        public BigInteger GetWholePart()
        {
            return BigInteger.Divide(m_numerator, Denominator);
        }

        public BigRational GetFractionPart()
        {
            return new BigRational(BigInteger.Remainder(m_numerator, Denominator), Denominator);
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            if (!(obj is BigRational))
                return false;
            return Equals((BigRational) obj);
        }

        public override int GetHashCode()
        {
            return (m_numerator / Denominator).GetHashCode();
        }

                int IComparable.CompareTo(object obj)
        {
            if (obj == null)
                return 1;
            if (!(obj is BigRational))
                throw new ArgumentException("Argument must be of type BigRational", "obj");
            return Compare(this, (BigRational) obj);
        }

                public int CompareTo(BigRational other)
        {
            return Compare(this, other);
        }

                public override string ToString()
        {
            var ret = new StringBuilder();
            ret.Append(m_numerator.ToString("R", CultureInfo.InvariantCulture));
            ret.Append(c_solidus);
            ret.Append(Denominator.ToString("R", CultureInfo.InvariantCulture));
            return ret.ToString();
        }

                        public bool Equals(BigRational other)
        {
            if (Denominator == other.Denominator)
                return m_numerator == other.m_numerator;
            return m_numerator * other.Denominator == Denominator * other.m_numerator;
        }

        #endregion Public Instance Methods

        
        #region Constructors

        public BigRational(BigInteger numerator)
        {
            m_numerator = numerator;
            Denominator = BigInteger.One;
        }

                public BigRational(double value)
        {
            if (double.IsNaN(value))
                throw new ArgumentException("Argument is not a number", nameof(value));
            if (double.IsInfinity(value))
                throw new ArgumentException("Argument is infinity", nameof(value));

            SplitDoubleIntoParts(value, out var sign, out var exponent, out var significand, out _);

            if (significand == 0)
            {
                this = Zero;
                return;
            }

            m_numerator = significand;
            Denominator = 1 << 52;

            if (exponent > 0)
                m_numerator = BigInteger.Pow(m_numerator, exponent);
            else if (exponent < 0)
                Denominator = BigInteger.Pow(Denominator, -exponent);
            if (sign < 0)
                m_numerator = BigInteger.Negate(m_numerator);
            Simplify();
        }

                                                public BigRational(decimal value)
        {
            var bits = decimal.GetBits(value);
            if (bits == null || bits.Length != 4 || (bits[3] & ~(DecimalSignMask | DecimalScaleMask)) != 0 ||
                (bits[3] & DecimalScaleMask) > 28 << 16)
                throw new ArgumentException("invalid Decimal", nameof(value));

            if (value == decimal.Zero)
            {
                this = Zero;
                return;
            }

                        var ul = ((ulong) (uint) bits[2] << 32) | (uint) bits[1];             m_numerator = (new BigInteger(ul) << 32) | (uint) bits[0]; 
            var isNegative = (bits[3] & DecimalSignMask) != 0;
            if (isNegative)
                m_numerator = BigInteger.Negate(m_numerator);

                        var scale = (bits[3] & DecimalScaleMask) >> 16;             Denominator = BigInteger.Pow(10, scale);

            Simplify();
        }

        public BigRational(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.Sign == 0)
                throw new DivideByZeroException();
            if (numerator.Sign == 0)
            {
                                m_numerator = BigInteger.Zero;
                Denominator = BigInteger.One;
            }
            else if (denominator.Sign < 0)
            {
                m_numerator = BigInteger.Negate(numerator);
                Denominator = BigInteger.Negate(denominator);
            }
            else
            {
                m_numerator = numerator;
                Denominator = denominator;
            }
            Simplify();
        }

        public BigRational(BigInteger whole, BigInteger numerator, BigInteger denominator)
        {
            if (denominator.Sign == 0)
                throw new DivideByZeroException();
            if (numerator.Sign == 0 && whole.Sign == 0)
            {
                m_numerator = BigInteger.Zero;
                Denominator = BigInteger.One;
            }
            else if (denominator.Sign < 0)
            {
                Denominator = BigInteger.Negate(denominator);
                m_numerator = BigInteger.Negate(whole) * Denominator + BigInteger.Negate(numerator);
            }
            else
            {
                Denominator = denominator;
                m_numerator = whole * denominator + numerator;
            }
            Simplify();
        }

        #endregion Constructors

        
        #region Public Static Methods

        public static BigRational Abs(BigRational r)
        {
            return r.m_numerator.Sign < 0 ? new BigRational(BigInteger.Abs(r.m_numerator), r.Denominator) : r;
        }

        public static BigRational Negate(BigRational r)
        {
            return new BigRational(BigInteger.Negate(r.m_numerator), r.Denominator);
        }

        public static BigRational Invert(BigRational r)
        {
            return new BigRational(r.Denominator, r.m_numerator);
        }

        public static BigRational Add(BigRational x, BigRational y)
        {
            return x + y;
        }

        public static BigRational Subtract(BigRational x, BigRational y)
        {
            return x - y;
        }

        public static BigRational Multiply(BigRational x, BigRational y)
        {
            return x * y;
        }

        public static BigRational Divide(BigRational dividend, BigRational divisor)
        {
            return dividend / divisor;
        }

        public static BigRational Remainder(BigRational dividend, BigRational divisor)
        {
            return dividend % divisor;
        }

        public static BigRational DivRem(BigRational dividend, BigRational divisor, out BigRational remainder)
        {
                        
                        var ad = dividend.m_numerator * divisor.Denominator;
            var bc = dividend.Denominator * divisor.m_numerator;
            var bd = dividend.Denominator * divisor.Denominator;

            remainder = new BigRational(ad % bc, bd);
            return new BigRational(ad, bc);
        }

        public static BigRational Pow(BigRational baseValue, BigInteger exponent)
        {
            if (exponent.Sign == 0)
                return One;
            if (exponent.Sign < 0)
            {
                if (baseValue == Zero)
                    throw new ArgumentException("cannot raise zero to a negative power", nameof(baseValue));
                                baseValue = Invert(baseValue);
                exponent = BigInteger.Negate(exponent);
            }

            var result = baseValue;
            while(exponent > BigInteger.One)
            {
                result = result * baseValue;
                exponent--;
            }

            return result;
        }

                                                                                                public static BigInteger LeastCommonDenominator(BigRational x, BigRational y)
        {
                        return x.Denominator * y.Denominator / BigInteger.GreatestCommonDivisor(x.Denominator, y.Denominator);
        }

        public static int Compare(BigRational r1, BigRational r2)
        {
                        return BigInteger.Compare(r1.m_numerator * r2.Denominator, r2.m_numerator * r1.Denominator);
        }

        #endregion Public Static Methods

        #region Operator Overloads

        public static bool operator ==(BigRational x, BigRational y)
        {
            return Compare(x, y) == 0;
        }

        public static bool operator !=(BigRational x, BigRational y)
        {
            return Compare(x, y) != 0;
        }

        public static bool operator <(BigRational x, BigRational y)
        {
            return Compare(x, y) < 0;
        }

        public static bool operator <=(BigRational x, BigRational y)
        {
            return Compare(x, y) <= 0;
        }

        public static bool operator >(BigRational x, BigRational y)
        {
            return Compare(x, y) > 0;
        }

        public static bool operator >=(BigRational x, BigRational y)
        {
            return Compare(x, y) >= 0;
        }

        public static BigRational operator +(BigRational r)
        {
            return r;
        }

        public static BigRational operator -(BigRational r)
        {
            return new BigRational(-r.m_numerator, r.Denominator);
        }

        public static BigRational operator ++(BigRational r)
        {
            return r + One;
        }

        public static BigRational operator --(BigRational r)
        {
            return r - One;
        }

        public static BigRational operator +(BigRational r1, BigRational r2)
        {
                        return new BigRational(r1.m_numerator * r2.Denominator + r1.Denominator * r2.m_numerator,
                r1.Denominator * r2.Denominator);
        }

        public static BigRational operator -(BigRational r1, BigRational r2)
        {
                        return new BigRational(r1.m_numerator * r2.Denominator - r1.Denominator * r2.m_numerator,
                r1.Denominator * r2.Denominator);
        }

        public static BigRational operator *(BigRational r1, BigRational r2)
        {
                        return new BigRational(r1.m_numerator * r2.m_numerator, r1.Denominator * r2.Denominator);
        }

        public static BigRational operator /(BigRational r1, BigRational r2)
        {
                        return new BigRational(r1.m_numerator * r2.Denominator, r1.Denominator * r2.m_numerator);
        }

        public static BigRational operator %(BigRational r1, BigRational r2)
        {
                        return new BigRational(r1.m_numerator * r2.Denominator % (r1.Denominator * r2.m_numerator),
                r1.Denominator * r2.Denominator);
        }

        #endregion Operator Overloads

        
        #region explicit conversions from BigRational

        [CLSCompliant(false)]
        public static explicit operator sbyte(BigRational value)
        {
            return (sbyte) BigInteger.Divide(value.m_numerator, value.Denominator);
        }

        [CLSCompliant(false)]
        public static explicit operator ushort(BigRational value)
        {
            return (ushort) BigInteger.Divide(value.m_numerator, value.Denominator);
        }

        [CLSCompliant(false)]
        public static explicit operator uint(BigRational value)
        {
            return (uint) BigInteger.Divide(value.m_numerator, value.Denominator);
        }

        [CLSCompliant(false)]
        public static explicit operator ulong(BigRational value)
        {
            return (ulong) BigInteger.Divide(value.m_numerator, value.Denominator);
        }

        public static explicit operator byte(BigRational value)
        {
            return (byte) BigInteger.Divide(value.m_numerator, value.Denominator);
        }

        public static explicit operator short(BigRational value)
        {
            return (short) BigInteger.Divide(value.m_numerator, value.Denominator);
        }

        public static explicit operator int(BigRational value)
        {
            return (int) BigInteger.Divide(value.m_numerator, value.Denominator);
        }

        public static explicit operator long(BigRational value)
        {
            return (long) BigInteger.Divide(value.m_numerator, value.Denominator);
        }

        public static explicit operator BigInteger(BigRational value)
        {
            return BigInteger.Divide(value.m_numerator, value.Denominator);
        }

        public static explicit operator float(BigRational value)
        {
                                                return (float) (double) value;
        }

        public static explicit operator double(BigRational value)
        {
                                                if (SafeCastToDouble(value.m_numerator) && SafeCastToDouble(value.Denominator))
                return (double) value.m_numerator / (double) value.Denominator;

                        var denormalized = value.m_numerator * s_bnDoublePrecision / value.Denominator;
            if (denormalized.IsZero)
                return value.Sign < 0
                    ? BitConverter.Int64BitsToDouble(unchecked((long) 0x8000000000000000))
                    : 0d; 
            double result = 0;
            var isDouble = false;
            var scale = DoubleMaxScale;

            while(scale > 0)
            {
                if (!isDouble)
                    if (SafeCastToDouble(denormalized))
                    {
                        result = (double) denormalized;
                        isDouble = true;
                    }
                    else
                    {
                        denormalized = denormalized / 10;
                    }
                result = result / 10;
                scale--;
            }

            if (!isDouble)
                return value.Sign < 0 ? double.NegativeInfinity : double.PositiveInfinity;
            return result;
        }

        public static explicit operator decimal(BigRational value)
        {
                                                if (SafeCastToDecimal(value.m_numerator) && SafeCastToDecimal(value.Denominator))
                return (decimal) value.m_numerator / (decimal) value.Denominator;

                        var denormalized = value.m_numerator * s_bnDecimalPrecision / value.Denominator;
            if (denormalized.IsZero)
                return decimal.Zero;             for(var scale = DecimalMaxScale; scale >= 0; scale--)
                if (!SafeCastToDecimal(denormalized))
                {
                    denormalized = denormalized / 10;
                }
                else
                {
                    var dec = new DecimalUInt32();
                    dec.dec = (decimal) denormalized;
                    dec.flags = (dec.flags & ~DecimalScaleMask) | (scale << 16);
                    return dec.dec;
                }
            throw new OverflowException("Value was either too large or too small for a Decimal.");
        }

        #endregion explicit conversions from BigRational

        
        #region implicit conversions to BigRational

        [CLSCompliant(false)]
        public static implicit operator BigRational(sbyte value)
        {
            return new BigRational((BigInteger) value);
        }

        [CLSCompliant(false)]
        public static implicit operator BigRational(ushort value)
        {
            return new BigRational((BigInteger) value);
        }

        [CLSCompliant(false)]
        public static implicit operator BigRational(uint value)
        {
            return new BigRational((BigInteger) value);
        }

        [CLSCompliant(false)]
        public static implicit operator BigRational(ulong value)
        {
            return new BigRational((BigInteger) value);
        }

        public static implicit operator BigRational(byte value)
        {
            return new BigRational((BigInteger) value);
        }

        public static implicit operator BigRational(short value)
        {
            return new BigRational((BigInteger) value);
        }

        public static implicit operator BigRational(int value)
        {
            return new BigRational((BigInteger) value);
        }

        public static implicit operator BigRational(long value)
        {
            return new BigRational((BigInteger) value);
        }

        public static implicit operator BigRational(BigInteger value)
        {
            return new BigRational(value);
        }

        public static implicit operator BigRational(float value)
        {
            return new BigRational(value);
        }

        public static implicit operator BigRational(double value)
        {
            return new BigRational(value);
        }

        public static implicit operator BigRational(decimal value)
        {
            return new BigRational(value);
        }

        #endregion implicit conversions to BigRational

        
        #region serialization

        void IDeserializationCallback.OnDeserialization(object sender)
        {
            try
            {
                                if (Denominator.Sign == 0 || m_numerator.Sign == 0)
                {
                                                            m_numerator = BigInteger.Zero;
                    Denominator = BigInteger.One;
                }
                else if (Denominator.Sign < 0)
                {
                    m_numerator = BigInteger.Negate(m_numerator);
                    Denominator = BigInteger.Negate(Denominator);
                }
                Simplify();
            }
            catch(ArgumentException e)
            {
                throw new SerializationException("invalid serialization data", e);
            }
        }

        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            info.AddValue("Numerator", m_numerator);
            info.AddValue("Denominator", Denominator);
        }

        private BigRational(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            m_numerator = (BigInteger) info.GetValue("Numerator", typeof(BigInteger));
            Denominator = (BigInteger) info.GetValue("Denominator", typeof(BigInteger));
        }

        #endregion serialization

        
        #region instance helper methods

        private void Simplify()
        {
                                    if (m_numerator == BigInteger.Zero)
                Denominator = BigInteger.One;

            var gcd = BigInteger.GreatestCommonDivisor(m_numerator, Denominator);
            if (gcd > BigInteger.One)
            {
                m_numerator = m_numerator / gcd;
                Denominator = Denominator / gcd;
            }
        }

        #endregion instance helper methods

        
        #region static helper methods

        private static bool SafeCastToDouble(BigInteger value)
        {
            return s_bnDoubleMinValue <= value && value <= s_bnDoubleMaxValue;
        }

        private static bool SafeCastToDecimal(BigInteger value)
        {
            return s_bnDecimalMinValue <= value && value <= s_bnDecimalMaxValue;
        }

        private static void SplitDoubleIntoParts(double dbl, out int sign, out int exp, out ulong man,
            out bool isFinite)
        {
            DoubleUlong du;
            du.uu = 0;
            du.dbl = dbl;

            sign = 1 - ((int) (du.uu >> 62) & 2);
            man = du.uu & 0x000FFFFFFFFFFFFF;
            exp = (int) (du.uu >> 52) & 0x7FF;
            if (exp == 0)
            {
                                isFinite = true;
                if (man != 0)
                    exp = -1074;
            }
            else if (exp == 0x7FF)
            {
                                isFinite = false;
                exp = int.MaxValue;
            }
            else
            {
                isFinite = true;
                man |= 0x0010000000000000;                 exp -= 1075;
            }
        }

        private static double GetDoubleFromParts(int sign, int exp, ulong man)
        {
            DoubleUlong du;
            du.dbl = 0;

            if (man == 0)
            {
                du.uu = 0;
            }
            else
            {
                                var cbitShift = CbitHighZero(man) - 11;
                if (cbitShift < 0)
                    man >>= -cbitShift;
                else
                    man <<= cbitShift;

                                                exp += 1075;

                if (exp >= 0x7FF)
                {
                                        du.uu = 0x7FF0000000000000;
                }
                else if (exp <= 0)
                {
                                        exp--;
                    if (exp < -52)
                        du.uu = 0;
                    else
                        du.uu = man >> -exp;
                }
                else
                {
                                        du.uu = (man & 0x000FFFFFFFFFFFFF) | ((ulong) exp << 52);
                }
            }

            if (sign < 0)
                du.uu |= 0x8000000000000000;

            return du.dbl;
        }

        private static int CbitHighZero(ulong uu)
        {
            if ((uu & 0xFFFFFFFF00000000) == 0)
                return 32 + CbitHighZero((uint) uu);
            return CbitHighZero((uint) (uu >> 32));
        }

        private static int CbitHighZero(uint u)
        {
            if (u == 0)
                return 32;

            var cbit = 0;
            if ((u & 0xFFFF0000) == 0)
            {
                cbit += 16;
                u <<= 16;
            }
            if ((u & 0xFF000000) == 0)
            {
                cbit += 8;
                u <<= 8;
            }
            if ((u & 0xF0000000) == 0)
            {
                cbit += 4;
                u <<= 4;
            }
            if ((u & 0xC0000000) == 0)
            {
                cbit += 2;
                u <<= 2;
            }
            if ((u & 0x80000000) == 0)
                cbit += 1;
            return cbit;
        }

        #endregion static helper methods
    } } 