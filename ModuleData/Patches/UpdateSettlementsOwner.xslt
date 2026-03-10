<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
    <xsl:template match="@*|node()">
        <xsl:copy>
            <xsl:apply-templates select="@*|node()"/>
        </xsl:copy>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_KIN23']/@owner">
        <xsl:attribute name="owner">Faction.clan_japanese_1</xsl:attribute>
    </xsl:template>
    <xsl:template match="Settlement[@id='castle_KIN23']/@culture">
        <xsl:attribute name="culture">Culture.ninja</xsl:attribute>
    </xsl:template>
</xsl:stylesheet>